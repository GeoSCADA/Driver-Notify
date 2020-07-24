// Driver code for the Geo SCADA Notify demonstration

// This feature enables the user to acknowledge alarms in Twilio
// and the result of the acknowledge are fed back to the user.
#define FEATURE_ALARM_ACK


using System;
using System.Collections.Generic;
using System.Text;
using ClearSCADA.DBObjFramework;
using ClearSCADA.DriverFramework;
using Notify;
using System.Web.Script.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using System.Net;
using System.Web;

#if FEATURE_ALARM_ACK
// If including alarm acknowledge capability - requires a Reference to ClearScada.Client.dll
using ClearScada.Client;
#endif

namespace DriverNotify
{

    class DriverNotify
	{
		static void Main(string[] args)
		{

			using (DriverApp App = new DriverApp())
			{
				// Init the driver with the database module
				if (App.Init(new CSharpModule(), args))
				{
					// Do custom driver init here
					App.Log("Notify driver started");

					// Start the driver MainLoop
					App.MainLoop();
				}
			}
		}
	}



	public class DrvNotifyChannel : DriverChannel<NotifyChannel>
	{
		public void LogAndEvent(string Message)
		{
			Log(Message);
			if (this.DBChannel.EnhancedEvents)
			{
				App.SendReceiveObject(this.DBChannel.Id, OPCProperty.SendRecLogChannelEventText, Message);
			}
		}

		public override void OnDefine()
		{
			// Called when enabled or config changed. Not on startup
			base.OnDefine();

			LogAndEvent("Channel Defined: " + (string)DBChannel.RedirectorHost);
		}

		public override void OnConnect()
		{
			LogAndEvent("OnConnect Channel Online.");

			base.OnConnect();
		}

		public override void OnPoll()
		{
			LogAndEvent("Channel OnPoll: Online.");

			// No need to call base class. Return exception on failure of connection
			SetStatus(ServerStatus.Online, "Running");
		}

		public override void OnDisconnect()
		{
			LogAndEvent("Channel OnDisConnect()");

			base.OnDisconnect();
		}

		public override void OnUnDefine()
		{
			LogAndEvent("Channel OnUndefine()");

			base.OnUnDefine();
		}


		public override void OnExecuteAction(ClearSCADA.DriverFramework.DriverTransaction Transaction)
		{
			LogAndEvent("Driver Action - channel.");
			switch (Transaction.ActionType)
			{
				default:
					base.OnExecuteAction(Transaction);
					break;
			}
		}
	}

	// The scanner has a dictionary of successful and failed acknowledgements
	public class CookieStatus
	{
		public bool Status;
		public DateTime UpdateTime;
	}

	public class DrvCSScanner : DriverScanner<NotifyScanner>
	{
		// Create a list of successful and failed acknowledgements
		Dictionary<long, CookieStatus> CookieStatusList = new Dictionary<long, CookieStatus>();

		// Driver overridden methods
		public override void OnReadConfiguration()
		{
			base.OnReadConfiguration();
		}

		public override SourceStatus OnDefine()
		{
			SetScanRate(DBScanner.NormalScanRate,
							DBScanner.NormalScanOffset);

			((DrvNotifyChannel)this.Channel).LogAndEvent("Scanner defined");

			return SourceStatus.Online;
		}

		public override void OnUnDefine()
		{
			base.OnUnDefine();
		}

		private static DateTime lastScan = DateTime.Now;
		public override void OnScan()
		{
			SetStatus(SourceStatus.Online);

			// Prevent super rapid scanning
			if (lastScan.AddSeconds(10) < DateTime.Now)
			{
				lastScan = DateTime.Now;
				// We are scanning for STATUS data from the Redirector web service
				// This will tell us what events to log for success/fail of messages
				// And it can advise us of alarm acknowledgements to process
				((DrvNotifyChannel)this.Channel).LogAndEvent("Poll the Redirector");
				using (WebClient client = new WebClient())
				{
					string requestaddress = WebServerAddress() + "/NotifyRequest/";
					string requestparams = "?type=" + WebUtility.UrlEncode("STATUS");

#if FEATURE_ALARM_ACK
					// We add to the parameters the information within the AlarmStatusList - the info of failed and successful acknowledgements
					// We add each alarm cookie to the request, but just delete ones which are old
					int paramIndex = 1;
					foreach( long Cookie in CookieStatusList.Keys)
					{
						// We will hold on to these for 100 seconds, then remove them (this time could be a configurable parameter).
						if ( DateTime.Compare( CookieStatusList[Cookie].UpdateTime.Add(TimeSpan.FromSeconds(100)), DateTime.UtcNow) > 0)
						{
							// Add to request
							string alarmackdata = "&acookie" + paramIndex.ToString() + "=" + Cookie.ToString() + "&astatus" + paramIndex.ToString() + "=" + (CookieStatusList[Cookie].Status ? "1" : "0");
							requestparams += alarmackdata;
							((DrvNotifyChannel)this.Channel).LogAndEvent("Return alarm ack status: " + alarmackdata);
						}
						else
						{
							CookieStatusList.Remove(Cookie);
						}
					}
#endif
					((DrvNotifyChannel)this.Channel).LogAndEvent("Send STATUS poll with data: " + requestparams);
					try
					{
						// Send/receive to the Redirector
						string response = client.DownloadString(requestaddress + requestparams);
						((DrvNotifyChannel)this.Channel).LogAndEvent("Received data: " + response.Length.ToString());

						// ERROR text is sent back by the Redirector if it can't access the Twilio service
						// If the Redirector reports an error then raise a scanner alarm
						if (response.StartsWith("ERROR"))
						{
							((DrvNotifyChannel)this.Channel).LogAndEvent("Received ERROR response from Redirector: " + response);

							App.SendReceiveObject(this.DBScanner.Id, OPCProperty.SendRecRaiseScannerAlarm, "Poll error from Redirector" + response);
						}
						else
						{
							// Success - clear scanner alarm if present
							App.SendReceiveObject(this.DBScanner.Id, OPCProperty.SendRecClearScannerAlarm, "");
							((DrvNotifyChannel)this.Channel).LogAndEvent("Received good response from Redirector." );

							// And process the received data
							// Unpack the request strings - one new line per response
							string[] responses = response.Split('\n');
							foreach( string responseLine in responses)
							{
								// If not empty
								if ((responseLine ?? "").Trim() != "")
								{
									((DrvNotifyChannel)this.Channel).LogAndEvent("Received this response from Redirector: " + responseLine);
									ProcessResponse(responseLine);
								}
							}
						}
					}
					catch (Exception e)
					{
						// Why this failed (e.g. 404)
						((DrvNotifyChannel)this.Channel).LogAndEvent("Failed to poll Redirector: " + e.Message );

						// Driver should fail and raise alarm if this occurs too many times
						//App.SendReceiveObject(this.DBScanner.Id, OPCProperty.SendRecRaiseScannerAlarm, e.Message);
					}
				}
			}
		}

		private void ProcessResponse( string urlencodedResponse)
		{
			// Handle each response sent back from Twilio via the Redirector
			// Responses are URLEncoded
			// Responses are designed in Twilio Flow
			Dictionary<string, string> responseParams = new Dictionary<string, string>();
			// Split response into & separated key/value pairs

			string[] paramArray = urlencodedResponse.Split('&');
			((DrvNotifyChannel)this.Channel).LogAndEvent("Response has parameters: " + paramArray.Length.ToString());
			foreach ( string param in paramArray)
			{
				string[] keyvalue = param.Split('=');
				if (keyvalue.Length == 2)
				{
					responseParams.Add( WebUtility.UrlDecode(keyvalue[0]), WebUtility.UrlDecode(keyvalue[1]));
				}
			}
			// Read common parameters
			string Phone = responseParams["phone"] ?? "";
			((DrvNotifyChannel)this.Channel).LogAndEvent("phone:" + Phone);

			string Message = responseParams["message"] ?? "";
			((DrvNotifyChannel)this.Channel).LogAndEvent("message:" + Message);

			string AlarmCookie = responseParams["cookie"] ?? "";
			((DrvNotifyChannel)this.Channel).LogAndEvent("cookie:" + AlarmCookie);

			string responseType = responseParams["type"] ?? "";
			((DrvNotifyChannel)this.Channel).LogAndEvent("type:" + responseType);

			// Check parameter messagetype
			switch (responseType)
			{
				case "ERRORMESSAGE":
					App.SendReceiveObject(this.DBScanner.Id, OPCProperty.SendRecLogEventText, "Notify Error: " + Message + ", Phone: " + Phone);
					break;
#if FEATURE_ALARM_ACK
				case "ACKALARM":
					string UserId = responseParams["userid"] ?? "";
					((DrvNotifyChannel)this.Channel).LogAndEvent("userid:" + UserId);

					string PIN = responseParams["pin"] ?? "";
					((DrvNotifyChannel)this.Channel).LogAndEvent("pin:" + PIN); // Remove this when implementing.

					// To return the status of the ack.
					CookieStatus ThisCookieStatus = new CookieStatus();

					long AlarmCookieNum = 0;
					long.TryParse(AlarmCookie, out AlarmCookieNum);
					if (AlarmCookieNum == 0)
					{
						((DrvNotifyChannel)this.Channel).LogAndEvent("Invalid cookie");
						App.SendReceiveObject(this.DBScanner.Id, OPCProperty.SendRecLogEventText, "Alarm Acknowledge Error: Invalid cookie. User: " + UserId + ", Phone: " + Phone);
						break;
					}
					if ( TryAlarmAck(UserId, PIN, AlarmCookieNum, Phone) )
					{
						((DrvNotifyChannel)this.Channel).LogAndEvent("Good acknowledge");
						App.SendReceiveObject(this.DBScanner.Id, OPCProperty.SendRecLogEventText, "Alarm Acknowledged. Phone: " + Phone);
						ThisCookieStatus.Status = true;
					}
					else
					{
						((DrvNotifyChannel)this.Channel).LogAndEvent("Failed acknowledge");
						App.SendReceiveObject(this.DBScanner.Id, OPCProperty.SendRecLogEventText, "Alarm Acknowledge Error: " + UserId + ", Phone: " + Phone + ", Cookie: " + AlarmCookie);
						ThisCookieStatus.Status = false;
					}
					// Arrived here - so hopefully the alarm was acknowledged OK
					// Bundle the alarm cookie and current time into a list for sending back to the server in a poll - the only way it will get to hear about it
					// Add to a list of successful and failed acknowledgements
					ThisCookieStatus.UpdateTime = DateTime.UtcNow;
					CookieStatusList.Add(AlarmCookieNum, ThisCookieStatus);

					break;
#endif
				// Further types
				default:
					break;
			}
		}

#if FEATURE_ALARM_ACK
		// Alarm acknowledgement
		private bool TryAlarmAck( string UserId, string PIN, long Cookie, string Phone)
		{
			// Log on to the .Net Client API with these details
			// This requires a Reference from the project to this dll
			ClearScada.Client.Simple.Connection connection;
			var node = new ClearScada.Client.ServerNode(ClearScada.Client.ConnectionType.Standard, "127.0.0.1", 5481);
			((DrvNotifyChannel)this.Channel).LogAndEvent("Acknowledge - connection created.");

			connection = new ClearScada.Client.Simple.Connection("Notify");
			try
			{
				((DrvNotifyChannel)this.Channel).LogAndEvent("Acknowledge - connecting.");
				connection.Connect(node);
			}
			catch (CommunicationsException)
			{
				((DrvNotifyChannel)this.Channel).LogAndEvent("Ack request - Unable to log in to ClearSCADA server using UserId and PIN.");
				return false;
			}
			if (!connection.IsConnected)
			{
				((DrvNotifyChannel)this.Channel).LogAndEvent("Acknowledge - failed connection.");
				return false;
			}
			using (var spassword = new System.Security.SecureString())
			{
				foreach (var c in PIN)
				{
					spassword.AppendChar(c);
				}
				try
				{
					((DrvNotifyChannel)this.Channel).LogAndEvent("Acknowledge - logging in.");
					connection.LogOn(UserId, spassword);
				}
				catch (AccessDeniedException)
				{
					((DrvNotifyChannel)this.Channel).LogAndEvent("Ack request - Access denied, incorrect user Id or PIN.");
					return false;
				}
				catch (PasswordExpiredException)
				{
					((DrvNotifyChannel)this.Channel).LogAndEvent("Ack request - credentials expired.");
					return false;
				}
			}
			// Get root object
			ClearScada.Client.Simple.DBObject root = null;
			try
			{
				((DrvNotifyChannel)this.Channel).LogAndEvent("Acknowledge - get database object.");
				root = connection.GetObject("$Root");
			}
			catch (Exception e)
			{
				((DrvNotifyChannel)this.Channel).LogAndEvent("Ack request - Cannot get root object. " + e.Message);
				return false;
			}
			object[] parameters = new object[2];
			parameters[0] = Cookie;
			parameters[1] = "By Phone";

			// Try acknowledging alarm
			try
			{
				((DrvNotifyChannel)this.Channel).LogAndEvent("Acknowledge - accepting by cookie.");
				root.InvokeMethod("AcceptAlarmByCookie", parameters);
			}
			catch (Exception e)
			{
				((DrvNotifyChannel)this.Channel).LogAndEvent("Ack request - Cannot acknowledge. " + e.Message);
				return false;
			}
			return true;
		}
#endif

		public override void OnExecuteAction(DriverTransaction Transaction)
		{
			((DrvNotifyChannel)this.Channel).LogAndEvent("Driver Action - scanner.");
			switch (Transaction.ActionType)
			{
				// Used as the database method to send alarms and other messages to Twilio via the Redirector
				case OPCProperty.DriverActionNotifyMessage:
					{
						string Message = (string) Transaction.get_Args(0);
						string PhoneNumber = (string)Transaction.get_Args(1);
						string MessageType = (string)Transaction.get_Args(2);
						string CookieStr = (string)Transaction.get_Args(3);

						((DrvNotifyChannel)this.Channel).LogAndEvent("Notify Message to: " + PhoneNumber + " using " + MessageType + " cookie " + CookieStr);

						using (WebClient client = new WebClient())
						{
							((DrvNotifyChannel)this.Channel).LogAndEvent("Send Request" );
							string requestaddress = WebServerAddress() + "/NotifyRequest/";
							string requestparams = "?key=" + WebUtility.UrlEncode( this.DBScanner.APIKey) + 
													"&phone=" + WebUtility.UrlEncode( PhoneNumber) + 
													"&message=" + WebUtility.UrlEncode( Message) +
													"&type=" + WebUtility.UrlEncode( MessageType ) +
													"&cookie=" + WebUtility.UrlEncode( CookieStr);
							try
							{
								string response = client.DownloadString(requestaddress + requestparams);
								((DrvNotifyChannel)this.Channel).LogAndEvent("Received data - length: " + response.Length.ToString());

								string partresponse = "";
								if (response.Length < 100)
									partresponse = response;
								else
									partresponse = response.Substring(0, 100);

								// If the Redirector reports an error then raise a scanner alarm
								if (response.StartsWith("ERROR"))
								{
									((DrvNotifyChannel)this.Channel).LogAndEvent("Received error." + partresponse);
									App.SendReceiveObject(this.DBScanner.Id, OPCProperty.SendRecRaiseScannerAlarm, "Error from Redirector" + response.Substring(0,100));
								}
								else
								{
									((DrvNotifyChannel)this.Channel).LogAndEvent("Response OK");
									// Success - clear alarm if present
									App.SendReceiveObject(this.DBScanner.Id, OPCProperty.SendRecClearScannerAlarm, "");
								}
							}
							catch (Exception e)
							{
								// Why this failed (e.g. 404)
								((DrvNotifyChannel)this.Channel).LogAndEvent("Failed to send to Redirector: " + e.Message.Substring(0, 100));

								// Driver should fail and raise alarm
								App.SendReceiveObject(this.DBScanner.Id, OPCProperty.SendRecRaiseScannerAlarm, e.Message.Substring(0, 100));
							}
						}
					}
					this.CompleteTransaction(Transaction, 0, "Notify Message Send Successful.");
					break;

#if FEATURE_ALARM_ACK
				case OPCProperty.DriverActionTestAlarmAck:

					string UserId = (string)Transaction.get_Args(0);
					string PIN = (string)Transaction.get_Args(1);
					string CookieString = (string)Transaction.get_Args(2);
					long AlarmCookie = long.Parse(CookieString);

					if ( TryAlarmAck( UserId,  PIN,  AlarmCookie,  "no phone"))
					{
						((DrvNotifyChannel)this.Channel).LogAndEvent("Test acknowledge success.");
					}
					else
					{
						((DrvNotifyChannel)this.Channel).LogAndEvent("Test acknowledge fail.");
					}
					this.CompleteTransaction(Transaction, 0, "Test Acknowledgement Successful.");
					break;
#endif
				default:
					base.OnExecuteAction(Transaction);
					break;
			}
		}
		private string WebServerAddress()
		{
			string addr = ((DrvNotifyChannel)Channel).DBChannel.WSProtocol == 0 ? "http" : "https";
			addr += "://" + ((DrvNotifyChannel)Channel).DBChannel.RedirectorHost + ":" +
					((DrvNotifyChannel)Channel).DBChannel.RedirectorPort.ToString();
			return addr;
		}

	}

}
