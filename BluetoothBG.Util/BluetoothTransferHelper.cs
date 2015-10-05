using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BluetoothBG.Util
{
    public interface IBluetoothTransferHelper
    {
        Task RegisterBackgroundTask(string bgWorkerTaskName, string bgWokerTaskEntryPoint);
        Task<DeviceInformationCollection> FindSupportedDevicesAsync(RfcommServiceId serviceId);
        Task SendMessageAsync(string deviceId, string message);
        Task SendFileAsync(string deviceId, IReadOnlyList<IStorageItem> storageItems);
        Task<IStorageFile> ReadFileAsync(StreamSocket socket, StorageFolder folder, string outputFilename = null);
        Task<string> ReadMessageAsync(StreamSocket socket);
    }
    public class BluetoothTransferHelper : IBluetoothTransferHelper
    {
        private static BluetoothTransferHelper _instance;
        private IBackgroundTaskRegistration bgWorkerTaskRegistration;

        public static BluetoothTransferHelper GetInstance()
        {
            if (_instance == null)
            {
                _instance = new BluetoothTransferHelper();
            }
            return _instance;
        }

        public async Task RegisterBackgroundTask(string bgWorkerTaskName, string bgWorkerTaskEntryPoint)
        {
            foreach (var task in BackgroundTaskRegistration.AllTasks)
            {
                if (task.Value.Name == bgWorkerTaskName)
                {
                    bgWorkerTaskRegistration = task.Value;
                    break;
                }
            }

            if (bgWorkerTaskRegistration != null)
            {
                //statusTextBlock.Text = "STATUS: Background worker already registered.";
                return;
            }
            else
            {
                BackgroundAccessStatus bgAccessStatus = await BackgroundExecutionManager.RequestAccessAsync();

                var builder = new BackgroundTaskBuilder();
                builder.TaskEntryPoint = bgWorkerTaskEntryPoint;
                builder.SetTrigger(new TimeTrigger(30, false));
                builder.Name = bgWorkerTaskName;

                try
                {
                    bgWorkerTaskRegistration = builder.Register();
                    //statusTextBlock.Text = "STATUS: BG Worker Registered";
                }
                catch (Exception)
                {

                    throw;
                }
            }
        }

        public async Task<DeviceInformationCollection> FindSupportedDevicesAsync(RfcommServiceId serviceId)
        {
            // Find all paired instances of the Rfcomm chat service and display them in a list
            DeviceInformationCollection chatServiceDeviceCollection = await DeviceInformation.FindAllAsync(
                RfcommDeviceService.GetDeviceSelector(serviceId));

            if (chatServiceDeviceCollection == null || chatServiceDeviceCollection.Count == 0)
            {
                return null;
            }
            return chatServiceDeviceCollection;
        }

        public async Task SendMessageAsync(string deviceId, string message)
        {
            RfcommDeviceService messageService = await RfcommDeviceService.FromIdAsync(deviceId);
            int result = await ValidateConnection(messageService);
            if (result != 0) return;
            
            StreamSocket messageSocket;
            lock (this)
            {
                messageSocket = new StreamSocket();
            }
            try
            {
                await messageSocket.ConnectAsync(messageService.ConnectionHostName, messageService.ConnectionServiceName);
                DataWriter messageWriter = new DataWriter(messageSocket.OutputStream);
                messageWriter.WriteUInt32((uint)message.Length);
                messageWriter.WriteString(message);
                await messageWriter.StoreAsync();
            }
            catch (Exception)
            {

                throw;
            }
        }

        public async Task SendFileAsync(string deviceId, IReadOnlyList<IStorageItem> storageItems)
        {
            // right now just supporting delivering just one file
            if (storageItems.Count == 0) return;
            StorageFile storageFile = (StorageFile)storageItems.First();
            RfcommDeviceService messageService = await RfcommDeviceService.FromIdAsync(deviceId);
            int result = await ValidateConnection(messageService);
            if (result != 0) return;

            StreamSocket messageSocket;
            lock (this)
            {
                messageSocket = new StreamSocket();
            }
            try
            {
                await messageSocket.ConnectAsync(messageService.ConnectionHostName, messageService.ConnectionServiceName);                
                using (DataWriter messageWriter = new DataWriter(messageSocket.OutputStream))
                {

                    //var prop = await storageFile.GetBasicPropertiesAsync();                    
                    //// send file name length
                    //messageWriter.WriteInt32(storageFile.Name.Length);
                    //// send the file name
                    //messageWriter.WriteString(storageFile.Name);
                    //// send the file length
                    //messageWriter.WriteUInt64(prop.Size);
                    //// send the file
                    //var fileStream = await storageFile.OpenAsync(FileAccessMode.Read);
                    //Windows.Storage.Streams.Buffer buffer = new Windows.Storage.Streams.Buffer((uint)prop.Size);
                    //await fileStream.ReadAsync(buffer, (uint)prop.Size, InputStreamOptions.None);
                    //messageWriter.WriteBuffer(buffer);
                    string text = DateTime.Now.ToString();
                    messageWriter.WriteUInt32((uint)text.Length);
                    await messageWriter.StoreAsync();
                    messageWriter.WriteString(text);
                    //await messageWriter.StoreAsync();
                    //messageWriter.WriteUInt32((uint)buffer.Length);
                    //await messageWriter.StoreAsync();
                    //messageWriter.WriteBuffer(buffer);
                    await messageWriter.FlushAsync();
                    await messageWriter.StoreAsync();
                    await messageSocket.OutputStream.FlushAsync();
                }
            }
            catch (Exception)
            {

                throw;
            }
            finally
            {
                //messageSocket.Dispose();
            }
        }

        private static async Task<int> ValidateConnection(RfcommDeviceService messageService)
        {
            if (messageService == null)
            {
                //statusTextBlock.Text = "Access to the device is denied because the application was not granted access";
                return -1;
            }

            //Do various checks of the SDP record to make sure you are talking to a device that actually supports the Bluetooth Rfcomm Chat Service
            var attributes = await messageService.GetSdpRawAttributesAsync();
            if (!attributes.ContainsKey(Constants.SdpServiceNameAttributeId))
            {
                //statusTextBlock.Text =
                //    @"ERROR: The Chat service is not advertising the Service Name attribute (attribute id=0x100). " +
                //    "Please verify that you are running the BluetoothRfcommChat server.";
                //RunButton.IsEnabled = true;
                return -1;
            }

            var attributeReader = DataReader.FromBuffer(attributes[Constants.SdpServiceNameAttributeId]);
            var attributeType = attributeReader.ReadByte();
            if (attributeType != Constants.SdpServiceNameAttributeType)
            {
                //statusTextBlock.Text =
                //    "ERROR: The Chat service is using an unexpected format for the Service Name attribute. " +
                //    "Please verify that you are running the BluetoothRfcommChat server.";
                //RunButton.IsEnabled = true;
                return -1;
            }
            return 0;

            //var serviceNameLength = attributeReader.ReadByte();

            // The Service Name attribute requires UTF-8 encoding.
            //attributeReader.UnicodeEncoding = UnicodeEncoding.Utf8;
        }

        public async Task<IStorageFile> ReadFileAsync(StreamSocket socket, StorageFolder folder, string outputFilename = null)
        {
            StorageFile file;
            using (var rw = new DataReader(socket.InputStream))
            {
                // 1. Read the filename length
                await rw.LoadAsync(sizeof(Int32));
                var filenameLength = (uint)rw.ReadInt32();
                // 2. Read the filename
                await rw.LoadAsync(filenameLength);
                var originalFilename = rw.ReadString(filenameLength);
                if (outputFilename == null)
                {
                    outputFilename = originalFilename;
                }
                //3. Read the file length
                await rw.LoadAsync(sizeof(UInt64));
                var fileLength = rw.ReadUInt64();

                // 4. Reading file
                var buffer = rw.ReadBuffer((uint)fileLength);
                file = await ApplicationData.Current.LocalFolder.CreateFileAsync(outputFilename, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteBufferAsync(file, buffer);
                //using (var memStream = await DownloadFile(rw, fileLength))
                //{

                //    file = await folder.CreateFileAsync(outputFilename, CreationCollisionOption.ReplaceExisting);
                //    using (var fileStream1 = await file.OpenAsync(FileAccessMode.ReadWrite))
                //    {
                //        await RandomAccessStream.CopyAndCloseAsync(memStream.GetInputStreamAt(0), fileStream1.GetOutputStreamAt(0));
                //    }

                //    rw.DetachStream();
                //}
            }
            return file;
        }

        private async Task<InMemoryRandomAccessStream> DownloadFile(DataReader rw, ulong fileLength)
        {
            var memStream = new InMemoryRandomAccessStream();

            // Download the file
            while (memStream.Position < fileLength)
            {
                Debug.WriteLine(string.Format("Receiving file...{0}/{1} bytes", memStream.Position, fileLength));
                var lenToRead = Math.Min(Constants.BLOCK_SIZE, fileLength - memStream.Position);
                await rw.LoadAsync((uint)lenToRead);
                var tempBuff = rw.ReadBuffer((uint)lenToRead);
                await memStream.WriteAsync(tempBuff);
            }

            //var tempBuff = rw.ReadBuffer((uint)fileLength);
            //await memStream.WriteAsync(tempBuff);

            return memStream;
        }

        public async Task<string> ReadMessageAsync(StreamSocket socket)
        {
            DataReader reader = new DataReader(socket.InputStream);
            uint readLength = await reader.LoadAsync(sizeof(uint));
            if (readLength < sizeof(uint))
            {
                ApplicationData.Current.LocalSettings.Values["IsBackgroundTaskActive"] = false;             
            }
            uint currentLength = reader.ReadUInt32();

            readLength = await reader.LoadAsync(currentLength);
            if (readLength < currentLength)
            {
                ApplicationData.Current.LocalSettings.Values["IsBackgroundTaskActive"] = false;                
            }
            string message = reader.ReadString(currentLength);
            return message;
        }
    }
}
