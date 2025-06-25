using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.ViewManagement;

// Disable warning "...execution of the current method continues before the call is completed..."
#pragma warning disable 4014

// Disable warning to "consider using the 'await' operator to await non-blocking API calls"
#pragma warning disable 1998

namespace WpfGattTerminal
{
    public enum DEVICE_TYPE { NONE = 0, TRACKER, NRF_APP };

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public const int MAC_LENGTH = 17;           // MAC Format: 11:22:33:44:55:66
        public const int PIN_LENGTH = 6;

        private RegistrySetting registrySetting;
        private string MACAddress = string.Empty;
        public string PINCode { get; set; } = string.Empty;
        public string NameFilter { get; set; } = string.Empty;

        private DeviceConnected deviceConnected = null;

        //Handlers for device detection
        private DeviceWatcher deviceWatcher = null;
        private TypedEventHandler<DeviceWatcher, DeviceInformation> handlerAdded = null;
        private TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> handlerUpdated = null;
        private TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> handlerRemoved = null;
        private TypedEventHandler<DeviceWatcher, Object> handlerEnumCompleted = null;

        private DeviceWatcher blewatcher = null;
        private TypedEventHandler<DeviceWatcher, DeviceInformation> OnBLEAdded = null;
        private TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> OnBLEUpdated = null;
        private TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> OnBLERemoved = null;

        private TaskCompletionSource<String> syncWatcherTaskSrc;

        public ObservableCollection<DeviceInformationDisplay> ResultCollection { get; private set; }

        public MainWindow()
        {
            InitializeComponent();

            registrySetting = new RegistrySetting();

            ResultCollection = new ObservableCollection<DeviceInformationDisplay>();

            //Set DataContext for Data Binding
            DataContext = this;

            //Start Watcher for pairable/paired devices
            StartWatcher();

            msgTextBox.Clear();
            ShowVersion();
            inpTextBox.Focus();
        }

        ~MainWindow()
        {
            StopWatcher();
        }

        private void ShowVersion()
        {
            msgTextBox.AppendText("BLE GATT Terminal, Version 0.9.5x\r@2016-2017 Pegatron MCPDC\r");
            msgTextBox.AppendText($"Running on {registrySetting.s1} {registrySetting.s2} ({Environment.OSVersion.VersionString})\r");
            ///msgTextBox.AppendText($"resInitSec: {((App)Application.Current).resInitSec:X}\r");
        }

        private void msgTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            msgTextBox.ScrollToEnd();
        }

        private void inpTextBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string inpString = inpTextBox.Text.Trim();
                msgTextBox.AppendText($"\u23F5{inpString}\r");
                inpTextBox.Clear();

                ProcessCommand(inpString);
            }
        }

        private void myButton_Click(object sender, RoutedEventArgs e)
        {
            string inpString = ((Button)sender).Content.ToString();
            msgTextBox.AppendText($"{inpString}\r");
            inpTextBox.Clear();

            ProcessCommand(inpString);
            inpTextBox.Focus();
        }

        private readonly string helpMessage =
            "%help\t\t- display commands and usages\r" +
            "%getpin\t\t- display PIN code for accepting device on pairing request\r" +
            "%setpin\t\t- [PIN(xxxxxx)] input and replace with a new PIN code\r" +
            "%setting\t\t- launch the Windows Settings app\r" +
            "%pair\t\t- [MAC(xx:xx:xx:xx:xx:xx)]\r" +
            "%unpair\t\t- unpair and disconnect the device\r";

        private async void ProcessCommand(string ins)
        {
            if (ins.Length == 0) return;

            string[] argvs = ins.Split(null);

            if (argvs[0].Equals("%help"))
            {
                msgTextBox.AppendText(helpMessage);
            }
            else if (argvs[0].Equals("%getpin"))
            {
                msgTextBox.AppendText($"Current PIN code = {PINCode}\r");
            }
            else if (argvs[0].Equals("%setpin"))
            {
                try
                {
                    SavePINCode(argvs);
                }
                catch (Exception e)
                {
                    msgTextBox.AppendText($"SetPIN: {e.Message}\r");
                }
            }
            else if (argvs[0].Equals("%setting"))
            {
                // Set the desired remaining view.
                var options = new LauncherOptions();
                options.DesiredRemainingView = ViewSizePreference.UseMore;
                // Launch the URI
                Dispatcher.Invoke(DispatcherPriority.Normal, new Action(async () =>
                {
                    var uri = new Uri(@"ms-settings:bluetooth");
                    var success = await Launcher.LaunchUriAsync(uri, options);
                }));
            }
            else if (argvs[0].Equals("%pair"))
            {
                try
                {
                    inpTextBox.IsEnabled = false;
                    btnGrid.IsEnabled = false;
                    SaveMACAddress(argvs);
                    await PairBleDevice();
                }
                catch (Exception e)
                {
                    msgTextBox.AppendText($"{e.Message}\r");
                }
                finally
                {
                    if (!inpTextBox.IsEnabled)
                    {
                        inpTextBox.IsEnabled = true;
                        btnGrid.IsEnabled = true;
                        inpTextBox.Focus();
                    }
                }
            }
            else if (argvs[0].Equals("%unpair"))
            {
                if (deviceConnected != null)
                {
                    UnpairDevice(deviceConnected);
                    StopBLEWatcher();
                    deviceConnected = null;
                }
                else
                {
                    msgTextBox.AppendText("No device is currently paired...\r");
                }
            }
            else if (argvs[0][0] != '%' && deviceConnected?.TxCharacteristic != null)
            {
                writeTracker(ins);
            }
            else
            {
                msgTextBox.AppendText("Unknown command\r");
            }
        }

        private void SavePINCode(string[] argvs)
        {
            string formatErr = "PIN code format error";
            String pattern = @"^(\d{6})$";
            String pincodestr;

            if (argvs.Count() <= 1)
            {
                PinCodeWindow MyPinCode = new PinCodeWindow();
                MyPinCode.ShowDialog();

                pincodestr = MyPinCode.getResultMsg();
                if (String.IsNullOrEmpty(pincodestr))
                {
                    return;
                }
                msgTextBox.AppendText($"%setpin {pincodestr}\r");
            }
            else
            {
                pincodestr = argvs[1];
            }

            if (pincodestr.Length != PIN_LENGTH) throw new Exception(formatErr);
            MatchCollection matches = Regex.Matches(pincodestr, pattern);
            if (matches.Count != 1) throw new Exception(formatErr);

            msgTextBox.AppendText($"PIN code changed: {PINCode} => {pincodestr}\r");
            PINCode = new String(pincodestr.ToCharArray());
        }

        private void SaveMACAddress(string[] argvs)
        {
            string formatErr = "MAC address format error";
            String pattern = @"^([\dA-F]{2}):([\dA-F]{2}):([\dA-F]{2}):([\dA-F]{2}):([\dA-F]{2}):([\dA-F]{2})$";

            if (argvs.Count() <= 1)
            {
                ListViewWindow MyListView = new ListViewWindow();
                MyListView.ShowDialog();

                var msg = MyListView.getResultMsg();
                if (String.IsNullOrEmpty(msg))
                {
                    throw new Exception("Warning: Please select one device to connect...");
                }
                else if (msg.Length < MAC_LENGTH)
                {
                    throw new Exception("Error: Cannot select un-named devices to connect...");
                }
                else
                {
                    MACAddress = msg;
                    msgTextBox.AppendText($"%pair {MACAddress}\r");
                    return;
                }
            }

            if (argvs[1].Length != MAC_LENGTH) throw new Exception(formatErr);
            MatchCollection matches = Regex.Matches(argvs[1], pattern, RegexOptions.IgnoreCase);
            if (matches.Count != 1) throw new Exception(formatErr);
            MACAddress = new String(argvs[1].ToCharArray());
        }

        private int CompareTargetDevice(DeviceInformationDisplay deviceInfoDisp)
        {
            if (String.IsNullOrEmpty(MACAddress)) return -1;

            string s = deviceInfoDisp.Id;
            s = s.Substring(s.Length - MACAddress.Length);
            return String.Compare(s, MACAddress, true);         //IgnoreCase
        }

        private async void PairingRequestedHandler(
            DeviceInformationCustomPairing sender,
            DevicePairingRequestedEventArgs args)
        {
            switch (args.PairingKind)
            {
                case DevicePairingKinds.ConfirmOnly:
                    // Windows itself will pop the confirmation dialog as part of "consent" if this is running on Desktop or Mobile
                    // If this is an App for 'Windows IoT Core' where there is no Windows Consent UX, you may want to provide your own confirmation.
                    args.Accept();
                    break;

                case DevicePairingKinds.ProvidePin:
                    // A PIN may be shown on the target device and the user needs to enter the matching PIN on 
                    // this Windows device. Get a deferral so we can perform the async request to the user.
                    var collectPinDeferral = args.GetDeferral();

                    //Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
                    //{
                        args.Accept(PINCode);
                        collectPinDeferral.Complete();
                    //}));
                    break;
                    /*
                       case DevicePairingKinds.DisplayPin:
                           // We just show the PIN on this side. The ceremony is actually completed when the user enters the PIN
                           // on the target device. We automatically except here since we can't really "cancel" the operation
                           // from this side.
                           args.Accept();

                           // No need for a deferral since we don't need any decision from the user
                           Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
                           {
                               ShowPairingPanel(
                                   "Please enter this PIN on the device you are pairing with: " + args.Pin,
                                   args.PairingKind);

                           }));
                           break;

                    case DevicePairingKinds.ConfirmPinMatch:
                        // We show the PIN here and the user responds with whether the PIN matches what they see
                        // on the target device. Response comes back and we set it on the PinComparePairingRequestedData
                        // then complete the deferral.
                        var displayMessageDeferral = args.GetDeferral();

                        Dispatcher.Invoke(DispatcherPriority.Normal, new Action(async () =>
                        {
                            bool accept = await GetUserConfirmationAsync(args.Pin);
                            if (accept)
                            {
                                args.Accept();
                            }

                            displayMessageDeferral.Complete();
                        }));
                        break;
                   */
            }

            Debug.WriteLine($"DevicePairingRequestedEventArgs.PairingKind {args.PairingKind}");
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                msgTextBox.AppendText($"DevicePairingRequestedEventArgs.PairingKind {args.PairingKind}\r");
            }));
        }

        private async Task PairBleDevice()
        {
            DeviceInformationDisplay deviceInfoDisp = null;
            foreach (DeviceInformationDisplay d in ResultCollection)
            {
                if (CompareTargetDevice(d) == 0)
                {
                    deviceInfoDisp = d;
                    break;
                }
            }

            if (deviceInfoDisp == null)
            {
                msgTextBox.AppendText("No Matched Bluetooth LE Devices found\r");
                return;
            }

            if (deviceConnected != null)
            {
                msgTextBox.AppendText($"A device ({deviceConnected.Id}) was already paired.\r");
                return;
            }

            bool paired = true;
            if (deviceInfoDisp.IsPaired != true)
            {
                paired = false;
                msgTextBox.AppendText("Pairing device...\r");

                DevicePairingKinds ceremoniesSelected = DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin | DevicePairingKinds.ProvidePin | DevicePairingKinds.ConfirmPinMatch;
                DevicePairingProtectionLevel protectionLevel = DevicePairingProtectionLevel.Default;

                // Specify custom pairing with all ceremony types and protection level EncryptionAndAuthentication
                DeviceInformationCustomPairing customPairing = deviceInfoDisp.DeviceInformation.Pairing.Custom;

                customPairing.PairingRequested += PairingRequestedHandler;
                DevicePairingResult result = await customPairing.PairAsync(ceremoniesSelected, protectionLevel);

                customPairing.PairingRequested -= PairingRequestedHandler;

                if (result.Status == DevicePairingResultStatus.Paired)
                {
                    paired = true;
                }
                else
                {
                    msgTextBox.AppendText($"Pairing Failed {result.Status.ToString()}\r");
                }
            }

            if (paired)
            {
                // device is paired, set up the sensor Tag            
                msgTextBox.AppendText("Connecting device...\r");

                deviceConnected = new DeviceConnected(deviceInfoDisp);

                BluetoothLEDevice bluetoothLeDevice = null;
                try
                {
                    for (int i = 0; i < 12; i++)
                    {
                        // BT_Code: BluetoothLEDevice.FromIdAsync must be called from a UI thread because it may prompt for consent.
                        bluetoothLeDevice = await BluetoothLEDevice.FromIdAsync(deviceInfoDisp.Id);

                        if (bluetoothLeDevice != null)
                        {
                            if (deviceConnected.CheckDeviceType(bluetoothLeDevice) != DEVICE_TYPE.NONE)
                            {
                                break;
                            }
                            bluetoothLeDevice?.Dispose();
                            bluetoothLeDevice = null;
                        }
                        await Task.Delay(1000);
                    }
                }
                catch (Exception ex) when ((uint)ex.HResult == 0x800710df)
                {
                    // ERROR_DEVICE_NOT_AVAILABLE because the Bluetooth radio is not on.
                    bluetoothLeDevice = null;
                }

                if (bluetoothLeDevice != null)
                {
                    msgTextBox.AppendText($"Service count = {bluetoothLeDevice.GattServices.Count}\r");

                    deviceConnected.BleDevice = bluetoothLeDevice;

                    if (deviceConnected.DeviceType != DEVICE_TYPE.NONE)
                    {
                        syncWatcherTaskSrc = new TaskCompletionSource<string>();

                        //Start watcher for Bluetooth LE Services
                        StartBLEWatcher();

                        await syncWatcherTaskSrc.Task;
                        syncWatcherTaskSrc = null;                    }
                }
                else
                {
                    msgTextBox.AppendText("Error: Failed to connect to device.\r");
                }
            }
        }

        //Watcher for Bluetooth LE Services
        private void StartBLEWatcher()
        {
            // Hook up handlers for the watcher events before starting the watcher
            OnBLEAdded = async (watcher, deviceInfo) =>
            {
                Dispatcher.Invoke(DispatcherPriority.Normal, new Action(async () =>
                {
                    Debug.WriteLine("OnBLEAdded: " + deviceInfo.Id);
                    GattDeviceService service = await GattDeviceService.FromIdAsync(deviceInfo.Id);
                    if ((service != null) && (service.Device.DeviceInformation.Id == deviceConnected.Id))
                    {
                        msgTextBox.AppendText($"Found Service: {service.Uuid}\r");

                        // Only the first service is to be accepted
                        if (service.Uuid.Equals(deviceConnected.GetServiceGuid()) && deviceConnected.DeviceService == null)
                        {
                            deviceConnected.DeviceService = service;
                            try
                            {
                                await enableSensor(deviceConnected);
                                if (deviceConnected.TxCharacteristic != null)
                                {
                                    msgTextBox.AppendText($"The {deviceConnected.DeviceType} device is on!\r");
                                }
                             }
                            catch (Exception ex)
                            {
                                msgTextBox.AppendText($"Something wrong! {ex.Message}\r");
                            }
                            finally
                            {
                                syncWatcherTaskSrc.SetResult(String.Empty);
                            }
                        }
                    }
                }));
            };

            OnBLEUpdated = async (watcher, deviceInfoUpdate) =>
            {
                Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
                {
                    Debug.WriteLine($"OnBLEUpdated: {deviceInfoUpdate.Id}");
                }));
            };

            OnBLERemoved = async (watcher, deviceInfoUpdate) =>
            {
                Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
                {
                    Debug.WriteLine("OnBLERemoved");
                }));
            };

            string aqs = "(" + GattDeviceService.GetDeviceSelectorFromUuid(deviceConnected.GetServiceGuid()) + ")";
            Debug.WriteLine(aqs);

            blewatcher = DeviceInformation.CreateWatcher(aqs);
            blewatcher.Added += OnBLEAdded;
            blewatcher.Updated += OnBLEUpdated;
            blewatcher.Removed += OnBLERemoved;
            blewatcher.Start();
        }

        private void StopBLEWatcher()
        {
            if (null != blewatcher)
            {
                blewatcher.Added -= OnBLEAdded;
                blewatcher.Updated -= OnBLEUpdated;
                blewatcher.Removed -= OnBLERemoved;

                if (DeviceWatcherStatus.Started == blewatcher.Status ||
                    DeviceWatcherStatus.EnumerationCompleted == blewatcher.Status)
                {
                    blewatcher.Stop();
                }

                blewatcher = null;
            }
        }

        // Enable and subscribe to specified GATT characteristic
        private async Task enableSensor(DeviceConnected deviceConnected)
        {
            Debug.WriteLine($"Begin enable service: {deviceConnected.DeviceType}");

            GattCharacteristic characteristic = null;
            if (deviceConnected.DeviceService != null)
            {
                // Turn on notifications
                IReadOnlyList<GattCharacteristic> characteristicList = null;
                characteristicList = deviceConnected.DeviceService.GetCharacteristics(deviceConnected.GetNotificationGuid(true));

                if (characteristicList != null)
                {
                    characteristic = characteristicList.First();
                    if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                    {
                        // While encryption is not required by all devices, if encryption is supported by the device,
                        // it can be enabled by setting the ProtectionLevel property of the Characteristic object.
                        // All subsequent operations on the characteristic will work over an encrypted link.
                        //characteristic.ProtectionLevel = GattProtectionLevel.EncryptionRequired;

                        // Register the event handler for receiving notifications
                        characteristic.ValueChanged += revTracker_ValueChanged;

                        // In order to avoid unnecessary communication with the device, determine if the device is already
                        // correctly configured to send notifications.
                        // By default ReadClientCharacteristicConfigurationDescriptorAsync will attempt to get the current
                        // value from the system cache and communication with the device is not typically required.
                        var currentDescriptorValue = await characteristic.ReadClientCharacteristicConfigurationDescriptorAsync();

                        // Set the notify enable flag
                        GattCommunicationStatus status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

                        // Save a reference to each active characteristic, so that handlers do not get prematurely killed
                        deviceConnected.RxCharacteristic = characteristic;
                    }

                    characteristicList = deviceConnected.DeviceService.GetCharacteristics(deviceConnected.GetNotificationGuid(false));
                    if (characteristicList != null)
                    {
                        characteristic = characteristicList.First();
                        if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write)
                            || characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
                        {

                            // Save a reference to each active characteristic, so that handlers do not get prematurely killed
                            deviceConnected.TxCharacteristic = characteristic;
                        }
                    }
                }
            }

            Debug.WriteLine("End enable sensor");
        }

#region
        /*** Version 0.7.4
        private string revStr = string.Empty;

        void revTracker_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs eventArgs)
        {
            byte[] bArray = new byte[eventArgs.CharacteristicValue.Length];
            DataReader.FromBuffer(eventArgs.CharacteristicValue).ReadBytes(bArray);
            string text = System.Text.Encoding.ASCII.GetString(bArray);

            revStr += text;
            if (bArray.Contains<byte>(0))
            {
                text = $"{revStr.Substring(0, revStr.IndexOf('\0'))}\r";
                //text = $"{revStr.Substring(0, revStr.IndexOf('\0')).Replace("\n", string.Empty)}\r";
                revStr = string.Empty;

                Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
                {
                    msgTextBox.Text += text;
                }));
            }
        }
        */
#endregion

        /* Immutability and the StringBuilder class
         * A String object is called immutable(read-only), because its value cannot be modified after it has been
         * created. Methods that appear to modify a String object actually return a new String object that contains
         * the modification. Because strings are immutable, string manipulation routines that perform repeated
         * additions or deletions to what appears to be a single string can exact a significant performance penalty.
         * https://msdn.microsoft.com/zh-tw/library/system.string(v=vs.110).aspx#Immutability
         */

        void revTracker_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs eventArgs)
        {
            byte[] bArray = new byte[eventArgs.CharacteristicValue.Length];
            DataReader.FromBuffer(eventArgs.CharacteristicValue).ReadBytes(bArray);

            if (bArray.Contains<byte>(0))
            {
                var i = Array.IndexOf<byte>(bArray, 0);
                bArray[i] = (byte)'\r';
            }

            Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
            {
                msgTextBox.AppendText(System.Text.Encoding.ASCII.GetString(bArray));
            }));
        }

        private async void writeTracker(string text)
        {
            GattWriteOption writeOption = GattWriteOption.WriteWithResponse;
            if (deviceConnected.TxCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
            {
                writeOption = GattWriteOption.WriteWithoutResponse;
            }

            using (var writer = new DataWriter())
            {
                var t = new StringBuilder(text).AppendLine();  // P:System.Environment.NewLine
                //var t = new StringBuilder(text);
                //if (deviceConnected.DeviceType == DEVICE_TYPE.TRACKER)
                //{
                //    t.AppendLine();  // P:System.Environment.NewLine
                //}
                do
                {
                    if (t.Length > 20)
                    {
                        writer.WriteString(t.ToString(0, 20));
                        t.Remove(0, 20);
                    }
                    else
                    {
                        writer.WriteString(t.ToString());
                        t.Clear();
                    }
                    await deviceConnected.TxCharacteristic.WriteValueAsync(writer.DetachBuffer(), writeOption);
                }
                while (t.Length != 0);
            }
        }

        private async void UnpairDevice(DeviceConnected deviceConnected)
        {
            try
            {
                Debug.WriteLine("Disable Sensor");
                await disableSensor(deviceConnected.RxCharacteristic);

                Debug.WriteLine("UnpairAsync");
                DeviceUnpairingResult dupr = await deviceConnected.DeviceInfo.Pairing.UnpairAsync();
                Debug.WriteLine($"Unpairing result = {dupr.Status}");
                msgTextBox.AppendText($"Unpairing result = {dupr.Status}\r");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unpair exception = {ex.Message}");
                msgTextBox.AppendText($"Unpair Failed: {ex.Message}\r");
            }
        }

        // Disable notifications to specified GATT characteristic
        private async Task disableSensor(GattCharacteristic characteristic)
        {
            Debug.WriteLine("Begin disable of sensor");

            // Disable notifications
            if (characteristic != null)
            {
                if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                {
                    GattCommunicationStatus status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                }
            }

            Debug.WriteLine("End disable for sensor");
        }

        private void StartWatcher()
        {
            Debug.WriteLine("Start Enumerating Bluetooth LE Devices in Background...");

            ResultCollection.Clear();

            // Request the IsPaired property so we can display the paired status in the UI
            string[] requestedProperties = { "System.Devices.Aep.IsPaired", "System.Devices.Aep.IsPresent" };
            //string[] requestedProperties = { "System.Devices.Aep.IsPaired" };

            //for bluetooth LE Devices
            string aqsFilter = "System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\"";

            deviceWatcher = DeviceInformation.CreateWatcher(
                aqsFilter,
                requestedProperties,
                DeviceInformationKind.AssociationEndpoint
                );

            // Hook up handlers for the watcher events before starting the watcher

            handlerAdded = async (watcher, deviceInfo) =>
            {
                // Since we have the collection databound to a UI element, we need to update the collection on the UI thread.
                Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
                {
                    Debug.WriteLine("Watcher Add: " + deviceInfo.Id);
                    ResultCollection.Add(new DeviceInformationDisplay(deviceInfo));
                }));
            };
            deviceWatcher.Added += handlerAdded;

            handlerUpdated = async (watcher, deviceInfoUpdate) =>
            {
                // Since we have the collection databound to a UI element, we need to update the collection on the UI thread.
                Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
                {
                    Debug.WriteLine("Watcher Update: " + deviceInfoUpdate.Id);
                    // Find the corresponding updated DeviceInformation in the collection and pass the update object
                    // to the Update method of the existing DeviceInformation. This automatically updates the object
                    // for us.
                    foreach (DeviceInformationDisplay deviceInfoDisp in ResultCollection)
                    {
                        if (deviceInfoDisp.Id == deviceInfoUpdate.Id)
                        {
                            deviceInfoDisp.Update(deviceInfoUpdate);
                            break;
                        }
                    }
                }));
            };
            deviceWatcher.Updated += handlerUpdated;

            handlerRemoved = async (watcher, deviceInfoUpdate) =>
            {
                // Since we have the collection databound to a UI element, we need to update the collection on the UI thread.
                Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
                {
                    Debug.WriteLine("Watcher Remove: " + deviceInfoUpdate.Id);
                    // Find the corresponding DeviceInformation in the collection and remove it
                    foreach (DeviceInformationDisplay deviceInfoDisp in ResultCollection)
                    {
                        if (deviceInfoDisp.Id == deviceInfoUpdate.Id)
                        {
                            ResultCollection.Remove(deviceInfoDisp);
                            if (ResultCollection.Count == 0)
                            {
                                Debug.WriteLine("Searching for Bluetooth LE Devices...");
                            }
                            break;
                        }
                    }
                }));
            };
            deviceWatcher.Removed += handlerRemoved;

            handlerEnumCompleted = async (watcher, obj) =>
            {
                Debug.WriteLine($"Found {ResultCollection.Count} Bluetooth LE Devices");
            };
            deviceWatcher.EnumerationCompleted += handlerEnumCompleted;

            deviceWatcher.Start();
        }

        private void StopWatcher()
        {
            if (null != deviceWatcher)
            {
                // First unhook all event handlers except the stopped handler. This ensures our
                // event handlers don't get called after stop, as stop won't block for any "in flight" 
                // event handler calls.  We leave the stopped handler as it's guaranteed to only be called
                // once and we'll use it to know when the query is completely stopped. 
                deviceWatcher.Added -= handlerAdded;
                deviceWatcher.Updated -= handlerUpdated;
                deviceWatcher.Removed -= handlerRemoved;
                deviceWatcher.EnumerationCompleted -= handlerEnumCompleted;

                if (DeviceWatcherStatus.Started == deviceWatcher.Status ||
                    DeviceWatcherStatus.EnumerationCompleted == deviceWatcher.Status)
                {
                    deviceWatcher.Stop();
                }
            }
        }
    }

    public class DeviceConnected
    {
        public DeviceConnected(DeviceInformationDisplay deviceInfoDisp)
        {
            DeviceInfo = deviceInfoDisp.DeviceInformation;
        }

        ~DeviceConnected()
        {
            Dispose();
        }

        public void Dispose()
        {
            BleDevice?.Dispose();
            DeviceService?.Dispose();
            DeviceInfo = null;
            BleDevice = null;
            DeviceService = null;
            TxCharacteristic = null;
            RxCharacteristic = null;
        }

        public DEVICE_TYPE CheckDeviceType(BluetoothLEDevice device)
        {
            try
            {
                // Null-conditional Operators help you write less code to handle null checks.
                if (device?.GattServices?.Count > 0)
                {
                    // BT_Code: GattServices returns a list of all the supported services of the device.
                    // If the services supported by the device are expected to change
                    // during BT usage, subscribe to the GattServicesChanged event.
                    foreach (var service in device.GattServices)
                    {
                        //Debug.WriteLine("service: " + service.Uuid);
                        if (service.Uuid.Equals(DIO_GUID))      // GUID turns to lowercase letters
                        {
                            return DeviceType = DEVICE_TYPE.TRACKER;
                        }
                        if (service.Uuid.Equals(NRF_GUID))
                        {
                            return DeviceType = DEVICE_TYPE.NRF_APP;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // It might throw an "System.NullReferenceException", sometimes device.GattServices is weird!
                Debug.WriteLine($"Exception thrown in DeviceConnected.CheckDeviceType: {ex.Message}");
            }
            return DEVICE_TYPE.NONE;
        }

        public Guid GetServiceGuid()
        {
            return (DeviceType == DEVICE_TYPE.NRF_APP) ? NRF_GUID : DIO_GUID;
        }

        public Guid GetNotificationGuid(bool Notify)
        {
            if (DeviceType == DEVICE_TYPE.NRF_APP)
            {
                return Notify ? NRF_NOTIFICATION_GUID : NRF_WRITE_GUID;
            }
            else
            {
                return DIO_NOTIFICATION_GUID;
            }
        }

        const string DIO_GUID_STR = "00005500-D102-11E1-9B23-00025B00A5A5";
        const string DIO_NOTIFICATION_GUID_STR = "00005501-D102-11E1-9B23-00025B00A5A5";
        readonly Guid DIO_GUID = new Guid(DIO_GUID_STR);
        readonly Guid DIO_NOTIFICATION_GUID = new Guid(DIO_NOTIFICATION_GUID_STR);

        const string NRF_GUID_STR = "6e400001-b5a3-f393-e0a9-e50e24dcca9e";
        const string NRF_WRITE_GUID_STR = "6e400002-b5a3-f393-e0a9-e50e24dcca9e";
        const string NRF_NOTIFICATION_GUID_STR = "6e400003-b5a3-f393-e0a9-e50e24dcca9e";
        readonly Guid NRF_GUID = new Guid(NRF_GUID_STR);
        readonly Guid NRF_WRITE_GUID = new Guid(NRF_WRITE_GUID_STR);
        readonly Guid NRF_NOTIFICATION_GUID = new Guid(NRF_NOTIFICATION_GUID_STR);

        public DEVICE_TYPE DeviceType = DEVICE_TYPE.NONE;

        public DeviceInformation DeviceInfo = null;

        public BluetoothLEDevice BleDevice = null;

        public GattDeviceService DeviceService = null;

        public GattCharacteristic TxCharacteristic = null;

        public GattCharacteristic RxCharacteristic = null;

        public string Id => DeviceInfo.Id;
    }
}


