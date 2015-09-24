using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BluetoothBG.BackgroundTask
{
    public sealed class BackgroundSendingTask : IBackgroundTask
    {
        // The background task registration for the background advertisement watcher 
        private IBackgroundTaskRegistration taskRegistration;
        // The watcher trigger used to configure the background task registration 
        private RfcommConnectionTrigger trigger;
        // A name is given to the task in order for it to be identifiable across context. 
        private string taskName = "BluetoothBG_ServerTask";
        // Entry point for the background task. 
        private string taskEntryPoint = "BluetoothBG.BackgroundTask.RfcommServerTask";

        private BackgroundTaskDeferral deferral = null;
        private IBackgroundTaskInstance taskInstance = null;

        private StreamSocket chatSocket = null;
        private DataWriter chatWriter = null;
        private RfcommDeviceService chatService = null;
        private DeviceInformationCollection chatServiceDeviceCollection = null;

        // Define the raw bytes that are converted into SDP record
        private byte[] sdpRecordBlob = new byte[]
        {
            0x35, 0x4a,  // DES len = 74 bytes

            // Vol 3 Part B 5.1.15 ServiceName
            // 34 bytes
            0x09, 0x01, 0x00, // UINT16 (0x09) value = 0x0100 [ServiceName]
            0x25, 0x1d,       // TextString (0x25) len = 29 bytes
                0x42, 0x6c, 0x75, 0x65, 0x74, 0x6f, 0x6f, 0x74, 0x68, 0x20,     // Bluetooth <sp>
                0x52, 0x66, 0x63, 0x6f, 0x6d, 0x6d, 0x20,                       // Rfcomm <sp>
                0x43, 0x68, 0x61, 0x74, 0x20,                                   // Chat <sp>
                0x53, 0x65, 0x72, 0x76, 0x69, 0x63, 0x65,                       // Service <sp>
            // Vol 3 Part B 5.1.15 ServiceDescription
            // 40 bytes
            0x09, 0x01, 0x01, // UINT16 (0x09) value = 0x0101 [ServiceDescription]
            0x25, 0x23,       // TextString (0x25) = 33 bytes,
                0x42, 0x6c, 0x75, 0x65, 0x74, 0x6f, 0x6f, 0x74, 0x68, 0x20,     // Bluetooth <sp>
                0x52, 0x66, 0x63, 0x6f, 0x6d, 0x6d, 0x20,                       // Rfcomm <sp>
                0x43, 0x68, 0x61, 0x74, 0x20,                                   // Chat <sp>
                0x53, 0x65, 0x72, 0x76, 0x69, 0x63, 0x65, 0x20,                  // Service <sp>
                0x69, 0x6e, 0x20, 0x43, 0x23                                    // in C#

        };

        // The Chat Server's custom service Uuid: 34B1CF4D-1069-4AD6-89B6-E161D79BE4D8
        Guid RfcommChatServiceUuid = Guid.Parse("34B1CF4D-1069-4AD6-89B6-E161D79BE4D8");
        // The Id of the Service Name SDP attribute
        const UInt16 SdpServiceNameAttributeId = 0x100;

        // The SDP Type of the Service Name SDP attribute.
        // The first byte in the SDP Attribute encodes the SDP Attribute Type as follows :
        //    -  the Attribute Type size in the least significant 3 bits,
        //    -  the SDP Attribute Type value in the most significant 5 bits.
        const byte SdpServiceNameAttributeType = (4 << 3) | 5;

        // The value of the Service Name SDP attribute
        const string SdpServiceName = "Bluetooth Rfcomm Chat Service";
        private string deviceName;

        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            this.taskInstance = taskInstance;
            this.deferral = taskInstance.GetDeferral();

            // Find all paired instances of the Rfcomm chat service and display them in a list
            chatServiceDeviceCollection = await DeviceInformation.FindAllAsync(
                RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(RfcommChatServiceUuid)));

            if (chatServiceDeviceCollection.Count > 0)
            {
                deviceName = chatServiceDeviceCollection.First().Name;                
            }

            if (deviceName != null)
                await SendMessage(chatServiceDeviceCollection.FirstOrDefault().Id);
            else
            {
                ApplicationData.Current.LocalSettings.Values["ReceivedMessage"] = "Device not found";
                deferral.Complete();
            }
        }

        private async Task SendMessage(string id)
        {
            chatService = await RfcommDeviceService.FromIdAsync(id);
            if (chatService == null)
            {
                ApplicationData.Current.LocalSettings.Values["ReceivedMessage"] = "Access to the device is denied because the application was not granted access";
                deferral.Complete();
            }

            var attributes = await chatService.GetSdpRawAttributesAsync();
            if (!attributes.ContainsKey(SdpServiceNameAttributeId))
            {
                ApplicationData.Current.LocalSettings.Values["ReceivedMessage"] =
                    @"ERROR: The Chat service is not advertising the Service Name attribute (attribute id=0x100). " +
                    "Please verify that you are running the BluetoothRfcommChat server.";
                deferral.Complete();
                //RunButton.IsEnabled = true;
                return;
            }

            var attributeReader = DataReader.FromBuffer(attributes[SdpServiceNameAttributeId]);
            var attributeType = attributeReader.ReadByte();
            if (attributeType != SdpServiceNameAttributeType)
            {
                ApplicationData.Current.LocalSettings.Values["ReceivedMessage"] =
                    "ERROR: The Chat service is using an unexpected format for the Service Name attribute. " +
                    "Please verify that you are running the BluetoothRfcommChat server.";
                deferral.Complete();
                //RunButton.IsEnabled = true;
                return;
            }

            var serviceNameLength = attributeReader.ReadByte();

            // The Service Name attribute requires UTF-8 encoding.
            //attributeReader.UnicodeEncoding = System.Text.UnicodeEncoding.Utf8;

            lock (this)
            {
                chatSocket = new StreamSocket();
            }
            try
            {
                await chatSocket.ConnectAsync(chatService.ConnectionHostName, chatService.ConnectionServiceName);

                chatWriter = new DataWriter(chatSocket.OutputStream);

                await SendMessageAsync();
                // Receive Message
                //DataReader chatReader = new DataReader(chatSocket.InputStream);
                //ReceiveStringLoop(chatReader);
            }
            catch (Exception ex)
            {
                ApplicationData.Current.LocalSettings.Values["ReceivedMessage"] = ex.ToString();
                deferral.Complete();
            }
        }

        private async Task SendMessageAsync()
        {
            string text = DateTime.Now.ToString();

            try
            {
                chatWriter.WriteUInt32((uint)text.Length);
                chatWriter.WriteString(text);
                await chatWriter.StoreAsync();
            }
            catch (Exception ex)
            {
                ApplicationData.Current.LocalSettings.Values["ReceivedMessage"] = ex.ToString();
            }
        }
    }
}
