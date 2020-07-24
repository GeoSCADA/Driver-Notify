// Common OPC definitions for the Notify driver
using System;

namespace Notify
{
	public class OPCProperty
	{
		public const UInt32 Base = 0x04608000;

		// Channel
		// RendRecs
		public const UInt32 SendRecClearChannelAlarm = OPCProperty.Base + 10;
		public const UInt32 SendRecRaiseChannelAlarm = OPCProperty.Base + 11;
		public const UInt32 SendRecLogChannelEventText = OPCProperty.Base + 12;
		// Actions

		// Scanner / FD
		// RendRecs
		public const UInt32 SendRecClearScannerAlarm = OPCProperty.Base + 12;
		public const UInt32 SendRecRaiseScannerAlarm = OPCProperty.Base + 14;
		public const UInt32 SendRecLogEventText = OPCProperty.Base + 15;
		public const UInt32 SendRecAckAlarm = OPCProperty.Base + 16;
		// Actions
		public const UInt32 DriverActionNotifyMessage = OPCProperty.Base + 88;
		public const UInt32 DriverActionTestAlarmAck = OPCProperty.Base + 89;

	}
}
