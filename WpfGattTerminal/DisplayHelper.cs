using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media.Imaging;
//using Windows.UI.Xaml.Media.Imaging;
using System.IO;
using System.Diagnostics;
using Windows.Devices.Enumeration;
using System.Reflection;

namespace WpfGattTerminal
{
    public class DeviceInformationDisplay : INotifyPropertyChanged
    {
        public DeviceInformationDisplay(DeviceInformation deviceInfoIn)
        {
            DeviceInformation = deviceInfoIn;
            UpdateGlyphBitmapImage();
        }

        private MainWindow rootWindow = App.Current.MainWindow as MainWindow;

        public DeviceInformation DeviceInformation { get; private set; }

        public DeviceInformationKind Kind => DeviceInformation.Kind;

        public string Id => DeviceInformation.Id;

        public string Name => DeviceInformation.Name;

        public BitmapImage GlyphBitmapImage { get; private set; }

        public bool CanPair => DeviceInformation.Pairing.CanPair;

        public bool IsPaired => DeviceInformation.Pairing.IsPaired;

        public IReadOnlyDictionary<string, object> Properties => DeviceInformation.Properties;

        public bool IsCollapsed
        {
            get
            {
                if (String.IsNullOrEmpty(rootWindow.NameFilter))
                {
                    return false;
                }
                return !Name.StartsWith(rootWindow.NameFilter);
            }
        }

        public void Update(DeviceInformationUpdate deviceInfoUpdate)
        {
            DeviceInformation.Update(deviceInfoUpdate);

            OnPropertyChanged("Kind");
            OnPropertyChanged("Id");
            OnPropertyChanged("Name");
            OnPropertyChanged("DeviceInformation");
            OnPropertyChanged("CanPair");
            OnPropertyChanged("IsPaired");
            OnPropertyChanged("IsCollapsed");

            UpdateGlyphBitmapImage();
        }

        private async void UpdateGlyphBitmapImage()
        {
            DeviceThumbnail deviceThumbnail = await DeviceInformation.GetGlyphThumbnailAsync();
            BitmapImage glyphBitmapImage = new BitmapImage();

            // Converting IRandomAccessStream to System.IO.Stream in a WPF App
            // https://www.eternalcoding.com/?p=183
            //await glyphBitmapImage.SetSourceAsync(deviceThumbnail);
            deviceThumbnail.Seek(0);
            using (var ioStream = deviceThumbnail.AsStream())
            {
                glyphBitmapImage.BeginInit();
                glyphBitmapImage.StreamSource = ioStream;
                glyphBitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                glyphBitmapImage.EndInit();
            }

            GlyphBitmapImage = glyphBitmapImage;
            OnPropertyChanged("GlyphBitmapImage");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class RegistrySetting
    {
        const string DEF_PINCODE_STR = "000000";
        const string NAME_FILTER_STR = "Name Filter";
        private MainWindow rootWindow = App.Current.MainWindow as MainWindow;
        private string myAppName = Assembly.GetEntryAssembly().ManifestModule.Name;
        private string keyPINCode;
        private string keyNameFilter;

        public string s1 = string.Empty;
        public string s2 = string.Empty;

        public RegistrySetting()
        {
            GetDefaultRegKeys();

            GetCurrentVersion();

            //Provide AppID registry key to patch for Win10.0.15063
            ProvideAppID();
        }

        private void GetDefaultRegKeys()
        {
            try
            {
                keyPINCode = Registry.GetValue($@"HKEY_CURRENT_USER\Software\MCPDC\{myAppName}", "PIN Code", DEF_PINCODE_STR).ToString();
            }
            catch (Exception e) ///when ((uint)e.HResult == 0x80004003)
            {
                Debug.WriteLine($"An exception thrown in RegistrySetting PINCode reading: {e.Message}");
                keyPINCode = DEF_PINCODE_STR;
            }

            try
            {
                keyNameFilter = Registry.GetValue($@"HKEY_CURRENT_USER\Software\MCPDC\{myAppName}", NAME_FILTER_STR, "").ToString();
            }
            catch (Exception e) ///when ((uint)e.HResult == 0x80004003)
            {
                Debug.WriteLine($"An exception thrown in RegistrySetting NameFiler reading: {e.Message}");
                keyNameFilter = String.Empty;
            }

            rootWindow.PINCode = keyPINCode;
            rootWindow.NameFilter = keyNameFilter.Trim();
        }

        ~RegistrySetting()
        {
            if (!keyNameFilter.Equals(rootWindow.NameFilter))
            {
                try
                {
                    using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("SOFTWARE", true))
                    {
                        RegistryKey key = registryKey.CreateSubKey($@"MCPDC\{myAppName}");
                        key.SetValue(NAME_FILTER_STR, rootWindow.NameFilter, RegistryValueKind.String);
                        key.Close();
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"An exception thrown in ~RegistrySetting: {e.Message}");
                }
            }
        }

        private void GetCurrentVersion()
        {
            try
            {
                s1 = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString();
                s2 = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild", "").ToString();
            }
            catch (Exception e)
            {
                Debug.WriteLine($"An exception thrown in GetCurrentVersion: {e.Message}");
            }
        }

        // Bluetooth: BluetoothLEDevice::FromIdAsync() does not complete on 10.0.15063
        // https://social.msdn.microsoft.com/Forums/en-US/58da3fdb-a0e1-4161-8af3-778b6839f4e1/bluetooth-bluetoothledevicefromidasync-does-not-complete-on-10015063?forum=wdk

        const string APP_GUID_STR = "{90018110-00D7-4C8B-BEB6-97B43788AFF7}";
        private byte[] AccessPermission = {
            1, 0, 4, 0x80, 0x9C, 0, 0, 0, 0xAC, 0, 0, 0, 0, 0, 0, 0, 0x14, 0,
            0, 0, 2, 0, 0x88, 0, 6, 0, 0, 0, 0, 0, 0x14, 0, 7, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0,
            5, 0xA, 0, 0, 0, 0, 0, 0x14, 0, 3, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 5, 0x12, 0, 0, 0,
            0, 0, 0x18, 0, 7, 0, 0, 0, 1, 2, 0, 0, 0, 0, 0, 5, 0x20, 0, 0, 0, 0x20, 2, 0, 0, 0,
            0, 0x18, 0, 3, 0, 0, 0, 1, 2, 0, 0, 0, 0, 0, 0xF, 2, 0, 0, 0, 1, 0, 0, 0, 0, 0,
            0x14, 0, 3, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 5, 0x13, 0, 0, 0, 0, 0, 0x14, 0, 3, 0, 0,
            0, 1, 1, 0, 0, 0, 0, 0, 5, 0x14, 0, 0, 0, 1, 2, 0, 0, 0, 0, 0, 5, 0x20, 0, 0, 0,
            0x20, 2, 0, 0, 1, 2, 0, 0, 0, 0, 0, 5, 0x20, 0, 0, 0, 0x20, 2, 0, 0
        };

        private bool GetAppID()
        {
            try
            {
                string appId = Registry.GetValue($@"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\AppID\{myAppName}", "AppID", "").ToString();
                var ap = Registry.GetValue($@"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\AppID\{APP_GUID_STR}", "AccessPermission", "");
                if (appId != APP_GUID_STR) return false;
                if (!ap.GetType().Equals(typeof(System.Byte[]))) return false;
            }
            catch (Exception e)
            {
                Debug.WriteLine($"An exception thrown in GetAppID: {e.Message}");
                return false;
            }
            return true;
        }

        private void ProvideAppID()
        {
            if (!GetAppID())
            {
                try // Exception is thrown without being running with elevated privileges
                {
                    using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\AppID", true))
                    {
                        RegistryKey key = registryKey.CreateSubKey(myAppName);
                        key.SetValue("AppID", APP_GUID_STR, RegistryValueKind.String);
                        key.Close();
                        key = registryKey.CreateSubKey(APP_GUID_STR);
                        key.SetValue("AccessPermission", AccessPermission, RegistryValueKind.Binary);
                        key.Close();
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"An exception thrown in ProvideAppID: {e.Message}");
                }
            }
        }
    }
}