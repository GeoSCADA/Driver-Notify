// Web service code for the Redirector web service

// This feature enables the user to acknowledge alarms in Twilio
// and the result of the acknowledge are fed back to the user.
#define FEATURE_ALARM_ACK

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using SimpleWebServer;


namespace Redirector
{
	class Program
	{
		// These parameters are required by Twilio
		// Account SID is used by Twilio to identify the account owner
		private static string AccountSID = Properties.Settings.Default.AccountSID;

		// The Account Authorization token is used for authentication
		// It is viewable from the Twilio web interface, and configured on the Notifier object in the Geo SCADA Driver
		// (We don't put the token in this program for security reasons - that comes from a configuration property of the driver scanner object).

		// The Flow ID is used to identify the flow we want to run.
		// You will need to replicate and/or customise the example flow in Twilio (see the ReadMe pdf linked with this code)
		// This flow is a message sender with optional with acknowledgement from the user
		private static string TwilioFlowId = Properties.Settings.Default.TwilioFlowId;

		// This is a phone number used by Twilio to send messages. You will need to select and lease this number from your Twilio account
		private static string FromNumber = Properties.Settings.Default.FromNumber;

		// These parameters are used to set up this web server
		// This is the host name, and is used by the Driver for the connection. This address is configured in the channel
		// Note that we are relying on this process being accessible by the Driver and by Twilio itself to receive alarm responses
		// It is not recommended to use *, please use the host name. 
		// If you set up driver and Redirector on the same host (not recommended) you can use "localhost" but no Twilio responses will be possible.
		// (See https://docs.microsoft.com/en-us/dotnet/api/system.net.httplistener?view=netframework-4.8)
		private static string WebHostName = Properties.Settings.Default.WebHostName;
		// This is the port of the server
		private static int WebPort = Properties.Settings.Default.WebPort;
		// This is the protocol - http. If you wish to use https then additional configuration will be needed, but this is recommended
		private static string WebProtocol = Properties.Settings.Default.WebProtocol;


		// A list consisting of status responses to be sent back to the server when it asks
		private static List<string> StatusResponses = new List<string>();
		private static Dictionary<string, string> CookieStatusList = new Dictionary<string, string>();

		static void Main(string[] args)
		{
			// In this Redirector server we define two web endpoints

			// This is used by the Notify Driver to receive requests from the driver
			string MyDriverURL = WebProtocol + "://" + WebHostName + ":" + WebPort.ToString() + "/NotifyRequest/";
			WebServer wsd = new WebServer(DriverSendResponse, MyDriverURL);
			wsd.Run();
			Console.WriteLine("Redirector driver webserver on: " + MyDriverURL);

			// This is used by the Twilio Service to receive responses
			string MyTwilioURL = WebProtocol + "://" + WebHostName + ":" + WebPort.ToString() + "/TwilioRequest/";
			WebServer wst = new WebServer(TwilioSendResponse, MyTwilioURL);
			wst.Run();
			Console.WriteLine("Redirector driver webserver on: " + MyTwilioURL);

			Console.WriteLine("Press a key to quit.");
			Console.ReadKey();
			wsd.Stop();
			wst.Stop();
		}

		// This server communicates with the driver at the endpoint /NotifyRequest/ and:
		// a) Gets alarm/messages for outgoing notification and forwards them to Twilio
		//			parameters: type = VOICE or SMS
		//								key is the API key for Twilio
		//								phone, message, cookie are for alarm notifications
		// b) Gets status data/requests from the driver and buffers ack requests and returns any Twilio statuses
		//						type =  STATUS (to pass Twilio results back)
		//							If the Geo SCADA server wants to advise Twilio the ack status, then
		//								acookie1 and astatus1, then 2,3 etc.
		public static string DriverSendResponse(HttpListenerRequest request)
		{
			Console.WriteLine("** DriverSendResponse at: " + DateTime.Now.ToShortTimeString());

			string requestType = request.QueryString["type"] ?? "";
			Console.WriteLine("type:" + requestType); // Type VOICE or SMS - or STATUS

			// Check this is not a regular STATUS request from the driver
			if (requestType != "STATUS")
			{
				string AuthToken = request.QueryString["key"] ?? "";
				Console.WriteLine("key:" + AuthToken.Substring(0, 3) + "..."); // API Key - not to be made public

				// Phone number - until you are paying Twilio, you have to register your own phone
				// for testing on the Twilio account, otherwise messages will not be sent. 
				string Phone = request.QueryString["phone"] ?? "";
				Console.WriteLine("phone:" + Phone);

				string Message = request.QueryString["message"] ?? "";
				Console.WriteLine("message:" + Message);

				string Cookie = request.QueryString["cookie"] ?? "";
				Console.WriteLine("cookie:" + Cookie); // Alarm cookie, is zero if alarms are not to be acknowledged.

				// Call the Twilio API
				using (WebClient client = new WebClient())
				{
					// From and To are mandatory parameters. The additional parameters are used within the Twilio Flow
					var reqparm = new System.Collections.Specialized.NameValueCollection
					{
						["From"] = FromNumber,
						["To"] = Phone,
						["Parameters"] = "{\"mymessage\":\"" + Message.Replace("\"", "\\\"") + "\"," +
										  "\"messagetype\":\"" + requestType + "\"," +
										  "\"alarmcookie\":\"" + Cookie + "\"}"
					};

					// Need to use AccountSID and AuthToken as username and password in HTTP Basic Authentication
					var encoded = Convert.ToBase64String(Encoding.GetEncoding("ISO-8859-1").GetBytes(AccountSID + ":" + AuthToken));
					client.Headers.Add("Authorization", "Basic " + encoded);

					client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
					client.Headers["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.142 Safari/537.36";
					try
					{
						Console.WriteLine("Send POST to Twilio");
						// POST the request	
						byte[] responsebytes = client.UploadValues(TwilioFlowId,
																	"POST",
																	reqparm);
						string responsebody = Encoding.UTF8.GetString(responsebytes);

						Console.WriteLine("Received data. Body: " + responsebody.Length.ToString() + " bytes, " + Encoding.ASCII.GetString(responsebytes));
					}
					catch (Exception e)
					{
						// Why this failed (e.g. 404)
						Console.WriteLine("Exception: " + e.Message + " ");
						// Should cause failure/alarm of the channel by responding with error
						// We don't send an error code back as our request itself was OK, we rely on the message content being interpreted.
						Console.WriteLine("Return ERROR to driver");
						return "ERROR " + e.Message;
					}
				}
				string response = string.Format("<HTML><BODY>NotifyRequest<br>{0}</BODY></HTML>", DateTime.Now);
				Console.WriteLine("Return response: " + response);
				return response;
			}
			else
			{
				// This is a status request - send back information received from Twilio

#if FEATURE_ALARM_ACK
				// If we support alarm acknowledgement, then we also receive in the request a list of alarm cookies and ack status for each
				// e.g. &acookie1=1234&astatus1=1
				// Read these into a list so we can respond with them to Twilio in TwilioSendResponse
				// We clear the list each time - they expire on the Geo SCADA server driver
				CookieStatusList.Clear();
				int paramIndex = 1;
				do
				{
					string ACookie = request.QueryString["acookie" + paramIndex.ToString()] ?? "";
					string AStatus = request.QueryString["astatus" + paramIndex.ToString()] ?? "";
					if (ACookie == "" || AStatus == "")
					{
						Console.WriteLine("No (more) cookie status to add.");
						break;
					}
					CookieStatusList.Add(ACookie, AStatus);
					paramIndex++;
					Console.WriteLine("Adding acknowledgement status for cookie: " + ACookie + ", status: " + AStatus);
				} while (paramIndex != 100); // Maximum
#endif

				// We send back a simple array of JSON strings containing the status or alarm ack requests from Twilio
				// These all have the cookie and the message text attached so they can be matched to the original request
				string WebResponse = "";
				foreach (string s in StatusResponses)
				{
					Console.WriteLine("Appending response to this request: " + s);
					WebResponse += s + "\n";
				}
				// And clear so they are only sent once
				StatusResponses.Clear();

				return WebResponse;
			}
		}

		// We use this server endpoint to receive status and acknowledge data from Twilio
		// Typical uses - for Twilio to respond with messages such as no response to phone call
		// /TwilioRequest/ with parameters:
		//		type = ERRORMESSAGE, phone, message
		//		type = ACKALARM, phone, cookie, userid, pin
		//		type = ACKCHECK (#if FEATURE_ALARM_ACK)
		public static string TwilioSendResponse(HttpListenerRequest request)
		{
			// Note - if the port/address of this server is left open to requesters other than Twilio, then anything can be received here
			// So we length-limit the response, and the driver must properly validate this string data on receipt.
			Console.WriteLine("** TwilioSendResponse at: " + DateTime.Now.ToShortTimeString());
#if FEATURE_ALARM_ACK
			// Twilio may ask (after asking for an ack) if an alarm has been acknowledged, we should find that info and respond, if passed to us
			string requestType = request.QueryString["type"] ?? "";
			Console.WriteLine("type:" + requestType);
			if (requestType == "ACKCHECK")
			{
				Console.WriteLine("Received ACKCHECK from Twilio.");
				string cookie = request.QueryString["cookie"] ?? "";
				if (CookieStatusList.ContainsKey( cookie))
				{
					Console.WriteLine("Sending result for cookie: " + cookie + ", = " + CookieStatusList[cookie]);
					// Return parameter to Twilio - success (1) or failure (0) to acknowledge
					return ("ackresponse=" + CookieStatusList[cookie]); 
				}
				// No response available yet - return 2.
				Console.WriteLine("No result for cookie: " + cookie );
				return ("ackresponse=2");
			}
#endif
			// Twilio is asking for an alarm ack, bundle parameters and send to driver later when it requests
			// 'Reserialise' the query string
			string SerializedQueryString = "";
			foreach( string Key in request.QueryString.AllKeys)
			{
				string Value = request.QueryString[Key] ?? "";
				// Other checks can be added here to constrain the Key or also the number of keys allowed
				if (Value.Length > 200)
				{
					Value = Value.Substring(0, 200);
				}
				SerializedQueryString += WebUtility.UrlEncode(Key) + "=" + WebUtility.UrlEncode( Value) + "&";
			}
			Console.WriteLine("Buffering Twilio Response:" + SerializedQueryString); // Remove PIN user data when implementing

			// When receiving data, we queue it up and send back to the driver when it next polls us
			// We can't directly contact the driver, as for security architecture reasons we don't want to expose it as a server
			// So here we just save the incoming responses in a list.
			StatusResponses.Add(SerializedQueryString);

			// Simple ack to Twilio - it needs a valid response otherwise errors are raised
			return "body=nothing";
		}
	}
}
