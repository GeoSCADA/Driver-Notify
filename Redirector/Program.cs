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
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace Redirector
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		static void Main(string[] args)
		{
			if (Environment.UserInteractive)
			{
				RedirectorService service1 = new RedirectorService(); // Add args to () if needed
				service1.TestStartupAndStop(args);
			}
			else
			{
				// Put the body of your old Main method here.  
				ServiceBase[] ServicesToRun;
				ServicesToRun = new ServiceBase[]
				{
					new RedirectorService()
				};
				ServiceBase.Run(ServicesToRun);
			}
		}
	}

	class RedirectorWebServer
	{
		// These parameters are used to set up this web server
		// This is the host name, and is used by the Driver for the connection. This address is configured in the channel
		// Note that we are relying on this process being accessible by the Driver and by Twilio itself to receive alarm responses
		// It is not recommended to use *, please use the host name. 
		// If you set up driver and Redirector on the same host (not recommended) you can use "localhost" but no Twilio responses will be possible.
		// (See https://docs.microsoft.com/en-us/dotnet/api/system.net.httplistener?view=netframework-4.8)
		private static string WebHostName = Properties.Settings.Default.WebHostName;
		// This is the port of the server - both can be the same if desired, but if different then the firewall settings can be better targeted
		private static int DriverWebPort = Properties.Settings.Default.DriverWebPort;
		private static int TwilioWebPort = Properties.Settings.Default.TwilioWebPort;
		// This is the protocol - http. If you wish to use https then additional configuration will be needed, but this is recommended
		private static string WebProtocol = Properties.Settings.Default.WebProtocol;

		// Web servers objects and event log parameters
		private static WebServer wsd;
		private static WebServer wst;
		private static System.Diagnostics.EventLog LMeventLog;
		private static int eventId;

		// A list consisting of status responses to be sent back to the server when it asks
		private static List<string> StatusResponses = new List<string>();
		private static Dictionary<string, string> CookieStatusList = new Dictionary<string, string>();

		public static void Start(System.Diagnostics.EventLog _LMeventLog, int _eventId)
		{
			LMeventLog = _LMeventLog;
			eventId = _eventId++;

			// In this Redirector server we define two web endpoints
			try
			{

				// This is used by the Notify Driver to receive requests from the driver
				string MyDriverURL = WebProtocol + "://" + WebHostName + ":" + DriverWebPort.ToString() + "/NotifyRequest/";
				wsd = new WebServer(DriverSendResponse, MyDriverURL);
				wsd.Run();
				LMeventLog.WriteEntry("Redirector to driver webserver on: " + MyDriverURL, EventLogEntryType.Information, eventId++);

				// This is used by the Twilio Service to receive responses
				string MyTwilioURL = WebProtocol + "://" + WebHostName + ":" + TwilioWebPort.ToString() + "/TwilioRequest/";
				wst = new WebServer(TwilioSendResponse, MyTwilioURL);
				wst.Run();
				LMeventLog.WriteEntry("Redirector to Twilio webserver on: " + MyTwilioURL, EventLogEntryType.Information, eventId++);
			}
			catch (Exception e)
			{
				LMeventLog.WriteEntry("Failure. Error starting webserver " + e.Message, EventLogEntryType.Error, eventId++);
			}
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
			string requestType = request.QueryString["type"] ?? "";
			//LMeventLog.WriteEntry("NotifyRequest type='" + requestType + "'", EventLogEntryType.Information, eventId++);

			// Check this is not a regular STATUS request from the driver
			if (requestType == "VOICE" || requestType == "SMS")
			{
				string logfilename = Properties.Settings.Default.WebLogPath + "WebLog-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log";
				StreamWriter logfile = File.CreateText(logfilename);

				LMeventLog.WriteEntry("DriverSendResponse logged to: " + logfilename, EventLogEntryType.Information, eventId++);

				try
				{
					logfile.WriteLine("DriverSendResponse Type: " + requestType); // Type VOICE or SMS - or STATUS

					// Config parameters from the driver
					string AccountSID = WebUtility.UrlDecode(request.QueryString["AccountSID"] ?? "");
					logfile.WriteLine("AccountSID:" + AccountSID);

					string FromNumber = WebUtility.UrlDecode(request.QueryString["FromNumber"] ?? "");
					logfile.WriteLine("FromNumber:" + FromNumber);

					string FlowID = WebUtility.UrlDecode(request.QueryString["FlowID"] ?? "");
					logfile.WriteLine("FlowID:" + FlowID);

					string APIKey = WebUtility.UrlDecode(request.QueryString["APIKey"] ?? "");
					if (APIKey.Length >= 3)
					{
						logfile.WriteLine("APIKey:" + APIKey.Substring(0, 3) + "..."); // API Key - not to be made public
					}

					// Phone number - until you are paying Twilio, you have to register your own phone
					// for testing on the Twilio account, otherwise messages will not be sent. 
					string Phone = WebUtility.UrlDecode(request.QueryString["phone"] ?? "");
					logfile.WriteLine("phone:" + Phone);

					string Message = WebUtility.UrlDecode(request.QueryString["message"] ?? "");
					logfile.WriteLine("message:" + Message);

					string Cookie = WebUtility.UrlDecode(request.QueryString["cookie"] ?? "");
					logfile.WriteLine("cookie:" + Cookie); // Alarm cookie, is zero if alarms are not to be acknowledged.

					// Security - The Flow ID/address must begin https://studio.twilio.com/
					if (!FlowID.StartsWith("https://studio.twilio.com/"))
					{
						logfile.WriteLine("Flow has incorrect address, must start with https://studio.twilio.com/");
					}
					else
					{
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
							var encoded = Convert.ToBase64String(Encoding.GetEncoding("ISO-8859-1").GetBytes(AccountSID + ":" + APIKey));
							client.Headers.Add("Authorization", "Basic " + encoded);

							client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
							client.Headers["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.142 Safari/537.36";
							try
							{
								logfile.WriteLine("Send POST to Twilio");
								// POST the request	
								byte[] responsebytes = client.UploadValues(FlowID,
																			"POST",
																			reqparm);
								string responsebody = Encoding.UTF8.GetString(responsebytes);

								logfile.WriteLine("Received data. Body: " + responsebody.Length.ToString() + " bytes, " + Encoding.ASCII.GetString(responsebytes));
							}
							catch (Exception e)
							{
								// Why this failed (e.g. 404)
								logfile.WriteLine("Exception: " + e.Message + " ");
								// Should cause failure/alarm of the channel by responding with error
								// We don't send an error code back as our request itself was OK, we rely on the message content being interpreted.
								logfile.WriteLine("Return ERROR to driver");
								logfile.Close();
								return "ERROR " + e.Message;
							}
						}
					}
					// Content of response not relevant
					string response = string.Format("<HTML><BODY>NotifyRequest<br>{0}</BODY></HTML>", DateTime.Now);
					logfile.WriteLine("Return response: " + response);
					logfile.Close();
					return response;
				}
				catch (Exception e)
				{
					LMeventLog.WriteEntry("Error getting Twilio parameters: " + e.Message, EventLogEntryType.Error, eventId++);
				}
			}
			else if (requestType == "STATUS")
			{
				string logtext = ""; // We don't want to log empty polls

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
						break;
					}
					CookieStatusList.Add(ACookie, AStatus);
					paramIndex++;

					logtext += "\nAdding acknowledgement status for cookie: " + ACookie + ", status: " + AStatus;
				} while (paramIndex != 100); // Maximum
#endif

				// We send back a simple array of JSON strings containing the status or alarm ack requests from Twilio
				// These all have the cookie and the message text attached so they can be matched to the original request
				string WebResponse = "";
				foreach (string s in StatusResponses)
				{
					logtext += "\nAppending response to this request: " + s;
					WebResponse += s + "\n";
				}
				// And clear so they are only sent once
				StatusResponses.Clear();

				// Only log if this is non-empty
				if (logtext != "")
				{
					string logfilename = Properties.Settings.Default.WebLogPath + "WebLog-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log";
					StreamWriter logfile = File.CreateText(logfilename);

					LMeventLog.WriteEntry("DriverSendResponse logged to: " + logfilename, EventLogEntryType.Information, eventId++);

					logfile.WriteLine("DriverSendResponse Type: " + requestType);
					logfile.WriteLine(logtext);
					logfile.WriteLine(WebResponse);

					logfile.Close();
				}
				return WebResponse;
			}

			LMeventLog.WriteEntry("Unknown Request Type", EventLogEntryType.Information, eventId++);
			return "Unknown Request Type";
		}

		// We use this server endpoint to receive status and acknowledge data from Twilio
		// Typical uses - for Twilio to respond with messages such as no response to phone call
		// /TwilioRequest/ with parameters:
		//		type = ERRORMESSAGE, phone, message
		//		type = ACKALARM, phone, cookie, userid, pin
		//		type = ACKCHECK (#if FEATURE_ALARM_ACK)
		public static string TwilioSendResponse(HttpListenerRequest request)
		{
			string logfilename = Properties.Settings.Default.WebLogPath + "WebLog-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log";

			LMeventLog.WriteEntry("TwilioSendResponse logged to: " + logfilename, EventLogEntryType.Information, eventId++);

			StreamWriter logfile = File.CreateText(logfilename);

			// Note - if the port/address of this server is left open to requesters other than Twilio, then anything can be received here
			// So we length-limit the response, and the driver must properly validate this string data on receipt.
#if FEATURE_ALARM_ACK
			// Twilio may ask (after asking for an ack) if an alarm has been acknowledged, we should find that info and respond, if passed to us
			string requestType = request.QueryString["type"] ?? "";
			logfile.WriteLine("type:" + requestType);

			if (requestType == "ACKCHECK") // Not yet in the Twilio example flow
			{
				logfile.WriteLine("Received ACKCHECK from Twilio.");
				string cookie = request.QueryString["cookie"] ?? "";
				if (CookieStatusList.ContainsKey( cookie))
				{
					logfile.WriteLine("Sending result for cookie: " + cookie + ", = " + CookieStatusList[cookie]);
					// Return parameter to Twilio - success (1) or failure (0) to acknowledge
					logfile.Close();
					return ("ackresponse=" + CookieStatusList[cookie]); 
				}
				// No response available yet - return 2.
				logfile.WriteLine("No result for cookie: " + cookie );
				logfile.Close();
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
			logfile.WriteLine("Buffering Twilio Response:" + SerializedQueryString); // Remove PIN user data when implementing

			// When receiving data, we queue it up and send back to the driver when it next polls us
			// We can't directly contact the driver, as for security architecture reasons we don't want to expose it as a server
			// So here we just save the incoming responses in a list.
			StatusResponses.Add(SerializedQueryString);

			// Simple ack to Twilio - it needs a valid response otherwise errors are raised
			logfile.Close();
			return "body=nothing";
		}
	}
}
