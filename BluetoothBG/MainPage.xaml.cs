using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace BluetoothBG
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        // The background task registration for the background advertisement watcher 
        private IBackgroundTaskRegistration taskRegistration;
        private IBackgroundTaskRegistration bgWorkerTaskRegistration;
        // The watcher trigger used to configure the background task registration 
        private RfcommConnectionTrigger trigger;
        // A name is given to the task in order for it to be identifiable across context. 
        private string taskName = "BluetoothBG_ServerTask";
        private string bgWorkerTaskName = "BluetoothBG_WorkerTask";
        // Entry point for the background task. 
        private string taskEntryPoint = "BluetoothBG.BackgroundTask.RfcommServerTask";
        private string bgWorkerTaskEntryPoint = "BluetoothBG.BackgroundTask.BackgroundSendingTask";

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
        public Guid RfcommChatServiceUuid = Guid.Parse("34B1CF4D-1069-4AD6-89B6-E161D79BE4D8");
        // The Id of the Service Name SDP attribute
        public const UInt16 SdpServiceNameAttributeId = 0x100;

        // The SDP Type of the Service Name SDP attribute.
        // The first byte in the SDP Attribute encodes the SDP Attribute Type as follows :
        //    -  the Attribute Type size in the least significant 3 bits,
        //    -  the SDP Attribute Type value in the most significant 5 bits.
        public const byte SdpServiceNameAttributeType = (4 << 3) | 5;

        // The value of the Service Name SDP attribute
        public const string SdpServiceName = "Bluetooth Rfcomm Chat Service";
        private string deviceName;

        public MainPage()
        {
            this.InitializeComponent();
            trigger = new RfcommConnectionTrigger();
            trigger.InboundConnection.LocalServiceId = RfcommServiceId.FromUuid(RfcommChatServiceUuid);

            // TODO:  helper function to create sdpRecordBlob
            trigger.InboundConnection.SdpRecord = sdpRecordBlob.AsBuffer();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            foreach (var task in BackgroundTaskRegistration.AllTasks)
            {
                if (task.Value.Name == taskName)
                {
                    AttachProgressAndCompletedHandlers(task.Value);
                }
            }
        }

        private async void startServerButton_Click(object sender, RoutedEventArgs e)
        {
            startServerButton.IsEnabled = false;

            // Registering a background trigger if it is not already registered. Rfcomm Chat Service will now be advertised in the SDP record
            // First get the existing tasks to see if we already registered for it

            foreach (var task in BackgroundTaskRegistration.AllTasks)
            {
                if (task.Value.Name == taskName)
                {
                    taskRegistration = task.Value;
                    break;
                }
            }

            if (taskRegistration != null)
            {
                statusTextBlock.Text = "STATUS: Background watcher already registered.";
                return;
            }
            else
            {
                // Applications registering for background trigger must request for permission.
                BackgroundAccessStatus backgroundAccessStatus = await BackgroundExecutionManager.RequestAccessAsync();

                var builder = new BackgroundTaskBuilder();
                builder.TaskEntryPoint = taskEntryPoint;
                builder.SetTrigger(trigger);
                builder.Name = taskName;

                try
                {
                    taskRegistration = builder.Register();
                    AttachProgressAndCompletedHandlers(taskRegistration);

                    // Even though the trigger is registered successfully, it might be blocked. Notify the user if that is the case.
                    if ((backgroundAccessStatus == BackgroundAccessStatus.Denied) || (backgroundAccessStatus == BackgroundAccessStatus.Unspecified))
                    {
                        statusTextBlock.Text = "ERROR: Not able to run in background. Application must given permission to be added to lock screen.";
                    }
                    else
                    {
                        statusTextBlock.Text = "STATUS: Background watcher registered.";
                    }
                }
                catch (Exception)
                {
                    statusTextBlock.Text = "STATUS: Background task not registered";
                }
            }
        }

        /// <summary>
        /// Called when background task defferal is completed.  This can happen for a number of reasons (both expected and unexpected).  
        /// IF this is expected, we'll notify the user.  If it's not, we'll show that this is an error.  Finally, clean up the connection by calling Disconnect().
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void OnCompleted(BackgroundTaskRegistration sender, BackgroundTaskCompletedEventArgs args)
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.ContainsKey("TaskCancelationReason"))
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    statusTextBlock.Text = "ERROR: Task cancelled unexpectedly - reason: " + settings.Values["TaskCancelationReason"].ToString();
                });
            }
            else
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    statusTextBlock.Text = "STATUS: Background task completed";
                });
            }
            try
            {
                args.CheckResult();
            }
            catch (Exception ex)
            {
                throw;
                //rootPage.NotifyUser(ex.Message, NotifyType.ErrorMessage);
            }
            Disconnect();
        }

        /// <summary>
        /// Handles UX changes and task registration changes when socket is disconnected
        /// </summary>
        private async void Disconnect()
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                //ListenButton.IsEnabled = true;
                //DisconnectButton.IsEnabled = false;
                //ConversationListBox.Items.Clear();

                // Unregistering the background task will remove the Rfcomm Chat Service from the SDP record and stop listening for incoming connections
                // First get the existing tasks to see if we already registered for it
                if (taskRegistration != null)
                {
                    taskRegistration.Unregister(true);
                    taskRegistration = null;
                    statusTextBlock.Text = "STATUS: Background watcher unregistered.";
                }
                else
                {
                    // At this point we assume we haven't found any existing tasks matching the one we want to unregister
                    statusTextBlock.Text = "STATUS: No registered background watcher found.";
                }
            });

        }

        /// <summary>
        /// The background task updates the progress counter.  When that happens, this event handler gets invoked
        /// When the handler is invoked, we will display the value stored in local settings to the user.
        /// </summary>
        /// <param name="task"></param>
        /// <param name="args"></param>
        private async void OnProgress(IBackgroundTaskRegistration task, BackgroundTaskProgressEventArgs args)
        {
            if (ApplicationData.Current.LocalSettings.Values.Keys.Contains("ReceivedMessage"))
            {
                string backgroundMessage = (string)ApplicationData.Current.LocalSettings.Values["ReceivedMessage"];
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    statusTextBlock.Text = "STATUS: Client Connected";
                    //ConversationListBox.Items.Add("Received: " + backgroundMessage);
                });
            }
        }

        private void AttachProgressAndCompletedHandlers(IBackgroundTaskRegistration task)
        {
            task.Progress += new BackgroundTaskProgressEventHandler(OnProgress);
            task.Completed += new BackgroundTaskCompletedEventHandler(OnCompleted);
        }

        /// <summary>
        /// When the user presses the run button, check to see if any of the currently paired devices support the Rfcomm chat service and display them in a list.  
        /// Note that in this case, the other device must be running the Rfcomm Chat Server before being paired.  
        /// </summary>
        /// <param name="sender">Instance that triggered the event.</param>
        /// <param name="e">Event data describing the conditions that led to the event.</param>
        private async void findDevices_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            // Disable the button while we do async operations so the user can't Run twice.
            button.IsEnabled = false;

            // Clear any previous messages
            statusTextBlock.Text = "";

            // Find all paired instances of the Rfcomm chat service and display them in a list
            chatServiceDeviceCollection = await DeviceInformation.FindAllAsync(
                RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(RfcommChatServiceUuid)));

            if (chatServiceDeviceCollection.Count > 0)
            {
                deviceName = chatServiceDeviceCollection.First().Name;
                statusTextBlock.Text = "STATUS: Found device: " + chatServiceDeviceCollection.FirstOrDefault().Name;
            }
            else
            {
                statusTextBlock.Text = "STATUS: No devices found!";
            }

            button.IsEnabled = true;
        }

        private async void sendMessage_Click(object sender, RoutedEventArgs e)
        {
            await SendMessage(chatServiceDeviceCollection.FirstOrDefault().Id);
        }

        private async Task SendMessage(string id)
        {
            //await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            //{
            chatService = await RfcommDeviceService.FromIdAsync(id);
            if (chatService == null)
            {
                statusTextBlock.Text = "Access to the device is denied because the application was not granted access";
                return;
            }

            //Do various checks of the SDP record to make sure you are talking to a device that actually supports the Bluetooth Rfcomm Chat Service
            var attributes = await chatService.GetSdpRawAttributesAsync();
            if (!attributes.ContainsKey(SdpServiceNameAttributeId))
            {
                statusTextBlock.Text =
                    @"ERROR: The Chat service is not advertising the Service Name attribute (attribute id=0x100). " +
                    "Please verify that you are running the BluetoothRfcommChat server.";
                //RunButton.IsEnabled = true;
                return;
            }

            var attributeReader = DataReader.FromBuffer(attributes[SdpServiceNameAttributeId]);
            var attributeType = attributeReader.ReadByte();
            if (attributeType != SdpServiceNameAttributeType)
            {
                statusTextBlock.Text =
                    "ERROR: The Chat service is using an unexpected format for the Service Name attribute. " +
                    "Please verify that you are running the BluetoothRfcommChat server.";
                //RunButton.IsEnabled = true;
                return;
            }

            var serviceNameLength = attributeReader.ReadByte();

            // The Service Name attribute requires UTF-8 encoding.
            attributeReader.UnicodeEncoding = UnicodeEncoding.Utf8;
            //ServiceName.Text = "Service Name: \"" + attributeReader.ReadString(serviceNameLength) + "\"";

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
            catch (Exception)
            {

                throw;
            }
            //});
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
            catch (Exception)
            {

                throw;
            }
        }

        private void fetchReceivedMessage_Click(object sender, RoutedEventArgs e)
        {
            var message = (string)ApplicationData.Current.LocalSettings.Values["ReceivedMessage"];
            statusTextBlock.Text = "RECEIVED MSG: " + message;
        }

        private async void registerBackgroundWorker_Click(object sender, RoutedEventArgs e)
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
                statusTextBlock.Text = "STATUS: Background worker already registered.";
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
                    statusTextBlock.Text = "STATUS: BG Worker Registered";
                }
                catch (Exception)
                {

                    throw;
                }
            }
        }

        private async void fetchReceivedFile_Click(object sender, RoutedEventArgs e)
        {
            var files = await ApplicationData.Current.LocalFolder.GetFilesAsync();
            int count = files.Count;
            statusTextBlock.Text = "NUMBER OF FILES: " + count;
        }
    }
}
