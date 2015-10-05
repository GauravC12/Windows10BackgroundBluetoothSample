using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Rfcomm;

namespace BluetoothBG.Util
{
    public class Constants
    {
        // The Chat Server's custom service Uuid: 34B1CF4D-1069-4AD6-89B6-E161D79BE4D8
        private static Guid RfcommChatServiceUuid = Guid.Parse("34B1CF4D-1069-4AD6-89B6-E161D79BE4D8");

        public static RfcommServiceId RfcommServiceIdentifier = RfcommServiceId.FromUuid(RfcommChatServiceUuid);

        // The Id of the Service Name SDP attribute
        public static UInt16 SdpServiceNameAttributeId = 0x100;

        // The SDP Type of the Service Name SDP attribute.
        // The first byte in the SDP Attribute encodes the SDP Attribute Type as follows :
        //    -  the Attribute Type size in the least significant 3 bits,
        //    -  the SDP Attribute Type value in the most significant 5 bits.
        public static byte SdpServiceNameAttributeType = (4 << 3) | 5;

        public static readonly uint BLOCK_SIZE = 1024;
    }
}
