// Database dll/extension for the Geo SCADA alarm notification demonstration

// This feature enables the user to acknowledge alarms in Twilio
// and the result of the acknowledge are fed back to the user.
#define FEATURE_ALARM_ACK

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Web.Script.Serialization;

using ClearSCADA.DBObjFramework;
using ClearSCADA.DriverFramework;


[assembly:Category("Notify")]
[assembly:Description("Notify Driver")]
[assembly:DriverTask("DriverNotify.exe")]

[System.ComponentModel.RunInstaller(true)]
public class CSInstaller :  DriverInstaller
{
}

namespace Notify
{
    public class CSharpModule : DBModule
    {
    }

    [Table("Notify Channel", "Notify")]
	[EventCategory("NotifyChannel", "Notify Channel", OPCProperty.Base + 0)]
	[EventCategory("NotifyChannel", "Notify Channel Debug", OPCProperty.Base + 81)]
	public class NotifyChannel : Channel
    {
        [AlarmCondition("NotifyChannelAlarm", "Notify", 0x03505041)]
        [AlarmSubCondition("NotifyChannelCommError")]
        public Alarm NotifyChannelAlarm;

        [Label("In Service", 1, 1)]
        [ConfigField("In Service", "Controls whether the channel is active.", 1, 2, 0x0350501B)]
        public override Boolean InService
        {
            get
            {
                return base.InService;
            }
            set
            {
                base.InService = value;
            }
        }

		[Label("Enhanced Events", 1, 3)]
		[ConfigField("Enhanced Events", "Controls whether debug messages are sent to the event log too.", 1, 4, OPCProperty.Base + 83)]
		public Boolean EnhancedEvents = true;

		[Label("Severity", 2, 1)]
        [ConfigField("Severity", "Severity", 2, 2, 0x0350501C)]
        public override ushort Severity
        {
            get
            {
                return base.Severity;
            }
            set
            {
                base.Severity = value;
            }
        }

        [Label("Area Of Interest", 3, 1, AreaOfInterest = true)]
        [ConfigField("Area Of Interest", "Reference to the Area Of Interest in which alarms & events on this object occur.", 3, 2, 0x03600D00)]
        public override AOIReference AreaOfInterestIdBase
        {
            get
            {
                return base.AreaOfInterestIdBase;
            }
            set
            {
                base.AreaOfInterestIdBase = value;
            }
        }

        [ConfigField("Area Of Interest", "Name of the Area Of Interest in which alarms & events on this object occur.", 3, 4, 0x03600D04, ReadOnly = true, Length = 48, Flags = FormFlags.Hidden)]
        public String AreaOfInterestBase
        {
            get { return AreaOfInterestIdBase.Name; }
        }

        [Label("Redirector Host", 4, 1)]
        [ConfigField("RedirectorHost",
                     "The IP address or network name of the alarm message redirector.",
                     4, 2, OPCProperty.Base + 1, Length = 80)]
        public string RedirectorHost;

        [Label("Redirector Port", 4, 3)]
        [ConfigField("RedirectorPort",
					 "The IP port of the message redirector.",
                     4, 4, OPCProperty.Base + 53)]
        public UInt16 RedirectorPort = 80;

		[Label("Protocol", 5, 1)]
		[ConfigField("WSProtocol",
						"Web service protocol.",
						5, 2, OPCProperty.Base + 10)]
		[Enum(new String[] { "HTTP", "HTTPS" })]
		public Byte WSProtocol = 0;

        public override void OnValidateConfig(MessageInfo Errors)
        {
            base.OnValidateConfig(Errors);
        }

        public override void OnReceive(uint Type, object Data, ref object Reply)
        {

            if (Type == OPCProperty.SendRecClearChannelAlarm)
            {
                if (NotifyChannelAlarm.ActiveSubCondition == "NotifyChannelCommError")
                {
                    NotifyChannelAlarm.Clear();
                    SetDataModified(true);
                }
            }
            else if (Type == OPCProperty.SendRecRaiseChannelAlarm)
            {
                // If not already active subcondition AND uncleared.
                if ((NotifyChannelAlarm.ActiveSubCondition != "NotifyChannelCommError") && (NotifyChannelAlarm.State != 4) && (NotifyChannelAlarm.State != 2))
                {
                    NotifyChannelAlarm.Raise("NotifyChannelCommError", "Notify Error: Redirector is Offline", Severity, true);
                    SetDataModified(true);
                }
            }
			else if (Type == OPCProperty.SendRecLogChannelEventText)
			{
				String Message = (String)Data;
				LogSystemEvent("NotifyChannel", Severity, Message);
			}
			else
			{
                base.OnReceive(Type, Data, ref Reply);
            }
        }

	}

	[Table("Notify Redirector", "Notify")]
    [EventCategory("Notify", "Notify", OPCProperty.Base + 3)]
	public class NotifyScanner : Scanner
    {
        [AlarmCondition("NotifyDeviceScannerAlarm", "Notify", 0x0350532F)]
        [AlarmSubCondition("NotifyCommError")]
        public Alarm NotifyScannerAlarm;

        [Label("Enabled", 1, 1)]
        [ConfigField("In Service", 
                     "Controls whether notification is active.",
                     1, 2, 0x350501B, DefaultOverride =true)]
        public override Boolean InService 
        {
            get 
            { 
                return base.InService;
            }
            set 
            { 
                base.InService = value;
            }
        }

        [Label("Severity", 2, 1)]
        [ConfigField("Severity", "Severity", 2, 2, 0x0350501C)]
        public override ushort Severity
        {
            get
            {
                return base.Severity;
            }
            set
            {
                base.Severity = value;
            }
		}

		[Label("Scan Interval", 1, 3)] 
		[ConfigField("ScanRate", "Scanning Interval (for reading alarm acknowledgement requests).", 1, 4, 0x03505045)] 
		[Interval(IntervalType.Seconds)]
		public UInt32 NormalScanRate = 60;

		[Label("Scan Offset", 2, 3)]
		[ConfigField("ScanOffset", "The scan offset", 2, 4, 0x0350504D, Length = 32)]
		public String NormalScanOffset = "M";

        [Label("Channel", 3, 1)]
        [ConfigField("ChannelId", "Channel Reference", 3, 2, 0x03505041)]
        public Reference<NotifyChannel> ChannelId;

        [Label("Area of Interest", 4, 1, AreaOfInterest = true)]
        [ConfigField("AOI Ref", "A reference to an AOI.", 4, 2, 0x0465700E)]
        public AOIReference AOIRef;

        [ConfigField("AOI Name", "A reference to an AOI.", 5, 3, 0x0465700F,
                     ReadOnly = true, Length = 48, Flags = FormFlags.Hidden)]
        public String AOIName
        {
            get { return AOIRef.Name; }
        }

		[Label("Service Type", 6, 1)]
		[ConfigField("ServiceType",
						"Type of service used.",
						6, 2, OPCProperty.Base + 17)]
		[Enum(new String[] { "Twilio Voice and SMS", "Microsoft Flow" })]
		public Byte ServiceType = 0;

		[Label("API Key", 7, 1)]
		[ConfigField("APIKey",
					 "The API key required by the redirector service. Leave blank if unused.",
					 7, 2, OPCProperty.Base + 54, Length = 80)]
		public string APIKey;

		[Label("Allow Acknowledge", 8, 1)]
		[ConfigField("AllowAck", "Controls whether alarms can be acknowledged.", 8, 2, OPCProperty.Base + 18)]
		public Boolean AllowAck = true;

		public override void OnValidateConfig(MessageInfo Errors)
        {
			//ToDo Check node not empty
            base.OnValidateConfig(Errors);
        }

		[Method("SMS Notify Message", "Send simple text message to redirector service.", OPCProperty.Base + 34)]
		public void NotifyMessageSMS(string Message,
												string UserVoicemailNumber)
		{
			object[] ArgObject = new Object[4];

			ArgObject[0] = Message;
			ArgObject[1] = UserVoicemailNumber;
			ArgObject[2] = "SMS";
			ArgObject[3] = "0"; // No alarm cookie for acknowledgement

			DriverAction(OPCProperty.DriverActionNotifyMessage, ArgObject, "SMS notify message: " + Message);
		}

		[Method("Voice Notify Message", "Send simple voice message to redirector service.", OPCProperty.Base + 33)]
		public void NotifyMessageVoice( string Message,
											      string UserVoicemailNumber)
		{
			object[] ArgObject = new Object[4];

			ArgObject[0] = Message;
			ArgObject[1] = UserVoicemailNumber;
			ArgObject[2] = "VOICE";
			ArgObject[3] = "0"; // No alarm cookie for acknowledgement

			DriverAction(OPCProperty.DriverActionNotifyMessage, ArgObject, "Voice notify message: " + Message);
		}

#if FEATURE_ALARM_ACK
		[Method("Voice Notify Alarm", "Send alarm as voice to redirector service and request acknowledge.", OPCProperty.Base + 35)]
		public void NotifyAlarmVoice( string Message,
											    string UserVoicemailNumber,
											    string Cookie)
		{
			object[] ArgObject = new Object[4];

			ArgObject[0] = Message;
			ArgObject[1] = UserVoicemailNumber;
			ArgObject[2] = "VOICE";
			ArgObject[3] = Cookie; // Allows alarm acknowledgement

			DriverAction(OPCProperty.DriverActionNotifyMessage, ArgObject, "Voice notify alarm: " + Message);
		}

		[Method("Test Acknowledge Alarm", "Acknowledge alarm via user details.", OPCProperty.Base + 36)]
		public void TestAlarmAck(string UserID,
												string PIN,
												string Cookie)
		{
			object[] ArgObject = new Object[3];

			ArgObject[0] = UserID;
			ArgObject[1] = PIN;
			ArgObject[2] = Cookie;

			DriverAction(OPCProperty.DriverActionTestAlarmAck, ArgObject, "Test alarm Ack: " + Cookie);
		}
#endif
		public override void OnReceive(uint Type, object Data, ref object Reply)
        {
            // Clear scanner alarm
            if (Type == OPCProperty.SendRecClearScannerAlarm)
            {
                NotifyScannerAlarm.Clear();
                SetDataModified(true);
            }
            // Set scanner alarm
            else if (Type == OPCProperty.SendRecRaiseScannerAlarm)
            {
                NotifyScannerAlarm.Raise("NotifyCommError", "Error: "+ (string)Data, Severity, true);
                SetDataModified(true);
            }
			// Ack alarm
			else if (Type == OPCProperty.SendRecAckAlarm)
			{
				// ToDo Ack
				SetDataModified(true);
			}
			else if (Type == OPCProperty.SendRecLogEventText)
			{
				// General debug message raised as event
				String Message = (String)Data;
				LogSystemEvent("NotifyScanner", Severity, Message);
			}
			else
				base.OnReceive(Type, Data, ref Reply);
        }
	}
}
