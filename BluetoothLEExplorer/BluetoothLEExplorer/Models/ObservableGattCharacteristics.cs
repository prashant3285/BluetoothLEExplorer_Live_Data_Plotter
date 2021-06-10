// <copyright file="ObservableGattCharacteristics.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//----------------------------------------------------------------------------------------------
using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BluetoothLEExplorer.Services.GattUuidHelpers;
using BluetoothLEExplorer.Services.Other;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using GattHelper.Converters;
using Windows.Networking.Sockets;
using Windows.Networking;
using Windows.System.Threading;
using System.Collections;

namespace BluetoothLEExplorer.Models
{
    /// <summary>
    /// Wrapper around <see cref="GattCharacteristic"/>  to make it easier to use
    /// </summary>
    public class ObservableGattCharacteristics : INotifyPropertyChanged
    {
        /// <summary>
        /// Enum used to determine how the <see cref="Value"/> should be displayed
        /// </summary>
        public enum DisplayTypes
        {
            NotSet,
            Bool,
            Decimal,
            Hex,
            UTF8,
            UTF16,
            Stream,
            Unsupported
        }

        /// <summary>
        /// Raw buffer of this value of this characteristic
        /// </summary>
        private IBuffer rawData;

        /// <summary>
        /// byte array representation of the characteristic value
        /// </summary>
        private byte[] data;
        public Queue qt = new Queue(); // *MOD* - Queue to collect incoming BLE data and pass on to TCPIP at a regular time period

        /// <summary>
        /// Source for <see cref="Characteristic"/>
        /// </summary>
        private GattCharacteristic characteristic;

        /// <summary>
        /// Gets or sets the characteristic this class wraps
        /// </summary>
        public GattCharacteristic Characteristic
        {
            get
            {
                return characteristic;
            }

            set
            {
                if (characteristic != value)
                {
                    characteristic = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("Characteristic"));
                }
            }
        }

        /// <summary>
        /// Source for <see cref="IsIndicateSet"/>
        /// </summary>
        private bool isIndicateSet = false;

        /// <summary>
        /// Gets or sets a value indicating whether indicate is set
        /// </summary>
        public bool IsIndicateSet
        {
            get
            {
                return isIndicateSet;
            }

            set
            {
                if (isIndicateSet != value)
                {
                    isIndicateSet = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("IsIndicateSet"));
                }
            }
        }

        /// <summary>
        /// Source for <see cref="IsNotifySet"/>
        /// </summary>
        private bool isNotifySet = false;

        /// <summary>
        /// Gets or sets a value indicating whether notify is set
        /// </summary>
        public bool IsNotifySet
        {
            get
            {
                return isNotifySet;
            }

            set
            {
                if (isNotifySet != value)
                {
                    isNotifySet = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("IsNotifySet"));
                }
            }
        }

        /// <summary>
        /// Source for <see cref="Parent"/>
        /// </summary>
        private ObservableGattDeviceService parent;

        /// <summary>
        /// Gets or sets the parent service of this characteristic
        /// </summary>
        public ObservableGattDeviceService Parent
        {
            get
            {
                return parent;
            }

            set
            {
                if (parent != value)
                {
                    parent = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("Parent"));
                }
            }
        }

        /// <summary>
        /// Source for <see cref="Characteristics"/>
        /// </summary>
        private ObservableCollection<ObservableGattDescriptors> descriptors = new ObservableCollection<ObservableGattDescriptors>();

        /// <summary>
        /// Gets or sets all the descriptors of this characterstic
        /// </summary>
        public ObservableCollection<ObservableGattDescriptors> Descriptors
        {
            get
            {
                return descriptors;
            }

            set
            {
                if (descriptors != value)
                {
                    descriptors = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("Descriptors"));
                }
            }
        }

        /// <summary>
        /// Source for <see cref="SelectedDescriptor"/>
        /// </summary>
        private ObservableGattDescriptors selectedDescriptor;

        /// <summary>
        /// Gets or sets the currently selected characteristic
        /// </summary>
        public ObservableGattDescriptors SelectedDescriptor
        {
            get
            {
                return selectedDescriptor;
            }

            set
            {
                if (selectedDescriptor != value)
                {
                    selectedDescriptor = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("SelectedDescriptor"));

                    // The SelectedProperty doesn't exist when this object is first created. This takes
                    // care of adding the correct event handler after the first time it's changed.
                    SelectedDescriptor_PropertyChanged();
                }
            }
        }
        /// <summary>
        /// Source for <see cref="Name"/>
        /// </summary>
        private string name;

        /// <summary>
        /// Gets or sets the name of this characteristic
        /// </summary>
        public string Name
        {
            get
            {
                return name;
            }

            set
            {
                if (name != value)
                {
                    name = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("Name"));
                }
            }
        }

        /// <summary>
        /// Source for <see cref="UUID"/>
        /// </summary>
        private string uuid;

        /// <summary>
        /// Gets or sets the UUID of this characteristic
        /// </summary>
        public string UUID
        {
            get
            {
                return uuid;
            }

            set
            {
                if (uuid != value)
                {
                    uuid = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("UUID"));
                }
            }
        }

        /// <summary>
        /// Source for <see cref="Value"/>
        /// </summary>
        private string value;

        /// <summary>
        /// Gets the value of this characteristic
        /// </summary>
        public string Value
        {
            get
            {
                return value;
            }

            private set
            {
                if (this.value != value)
                {
                    this.value = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("Value"));
                }
            }
        }

        /// <summary>
        /// Source for <see cref="DisplayType"/>
        /// </summary>
        private DisplayTypes displayType = DisplayTypes.NotSet;

        /// <summary>
        /// Gets or sets how this characteristic's value should be displayed
        /// </summary>
        public DisplayTypes DisplayType
        {
            get
            {
                return displayType;
            }

            set
            {
                if (value == DisplayTypes.NotSet)
                {
                    return;
                }

                if (displayType != value)
                {
                    displayType = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("DisplayType"));
                }
            }
        }

        /// <summary>
        /// Determines if the SelectedDescriptor_PropertyChanged has been added
        /// </summary>
        private bool hasSelectedDescriptorPropertyChangedHandler = false;

        /// <summary>
        /// Initializes a new instance of the<see cref="ObservableGattCharacteristics" /> class.
        /// </summary>
        /// <param name="characteristic">Characteristic this class wraps</param>
        /// <param name="parent">The parent service that wraps this characteristic</param>
        public ObservableGattCharacteristics(GattCharacteristic characteristic, ObservableGattDeviceService parent)
        {
            Characteristic = characteristic;
            Parent = parent;

            Name = GattCharacteristicUuidHelper.ConvertUuidToName(characteristic.Uuid);
            UUID = characteristic.Uuid.ToString();
        }

        public async Task Initialize()
        {
            await ReadValueAsync();
            await GetAllDescriptors();

            characteristic.ValueChanged += Characteristic_ValueChanged;
            PropertyChanged += ObservableGattCharacteristics_PropertyChanged;
        }

        /// <summary>
        /// Destruct this object by unsetting notification/indication and unregistering from property changed callbacks
        /// </summary>
        ~ObservableGattCharacteristics()
        {
            characteristic.ValueChanged -= Characteristic_ValueChanged;
            PropertyChanged -= ObservableGattCharacteristics_PropertyChanged;
            descriptors.Clear();

            Cleanup();
        }

        /// <summary>
        /// Cleanup this object by unsetting notification/indication
        /// </summary>
        private async void Cleanup()
        {
            await StopIndicate();
            await StopNotify();
        }

        /// <summary>
        /// Executes when this characteristic changes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ObservableGattCharacteristics_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "DisplayType")
            {
                SetValue();
            }
        }

        /// <summary>
        /// Reads the value of the Characteristic
        /// </summary>
        public async Task ReadValueAsync()
        {
            try
            {
                GattReadResult result = await characteristic.ReadValueAsync(
                    BluetoothLEExplorer.Services.SettingsServices.SettingsService.Instance.UseCaching ? BluetoothCacheMode.Cached : BluetoothCacheMode.Uncached);

                if (result.Status == GattCommunicationStatus.Success)
                {
                    SetValue(result.Value);
                }
                else if (result.Status == GattCommunicationStatus.ProtocolError)
                {
                    Value = Services.Other.GattProtocolErrorParser.GetErrorString(result.ProtocolError);
                }
                else
                {
                    Value = "Unreachable";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception: " + ex.Message);
                Value = "Unknown (exception: " + ex.Message + ")";
            }
        }

        /// <summary>
        /// Adds the SelectedDescriptor_PropertyChanged event handler
        /// </summary>
        private void SelectedDescriptor_PropertyChanged()
        {
            if (hasSelectedDescriptorPropertyChangedHandler == false)
            {
                SelectedDescriptor.PropertyChanged += SelectedDescriptor_PropertyChanged;
                hasSelectedDescriptorPropertyChangedHandler = true;
            }
        }

        /// <summary>
        /// Updates the selected characteristic in the app context
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SelectedDescriptor_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            GattSampleContext.Context.SelectedDescriptor = SelectedDescriptor;
        }

        private async Task GetAllDescriptors()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("ObservableGattCharacteristics::getAllDescriptors: ");
            sb.Append(Name);

            try
            {
                GattDescriptorsResult result = await characteristic.GetDescriptorsAsync(Services.SettingsServices.SettingsService.Instance.UseCaching ? BluetoothCacheMode.Cached : BluetoothCacheMode.Uncached);

                if (result.Status == GattCommunicationStatus.Success)
                {
                    sb.Append(" - found ");
                    sb.Append(result.Descriptors.Count);
                    sb.Append(" descriptors");
                    Debug.WriteLine(sb);
                    foreach (GattDescriptor descriptor in result.Descriptors)
                    {
                        ObservableGattDescriptors temp = new ObservableGattDescriptors(descriptor, this);
                        await temp.Initialize();
                        Descriptors.Add(temp);
                    }
                }
                else if (result.Status == GattCommunicationStatus.Unreachable)
                {
                    sb.Append(" - failed with Unreachable");
                    Debug.WriteLine(sb.ToString());
                }
                else if (result.Status == GattCommunicationStatus.ProtocolError)
                {
                    sb.Append(" - failed with ProtocolError");
                    Debug.WriteLine(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(" - Exception: {0}" + ex.Message);
                Value = "Unknown (exception: " + ex.Message + ")";
            }
        }

        /// <summary>
        /// Set's the indicate descriptor
        /// </summary>
        /// <returns>Set indicate task</returns>
        public async Task<bool> SetIndicate()
        {
            if (IsIndicateSet == true)
            {
                // already set
                return true;
            }

            try
            {
                // BT_Code: Must write the CCCD in order for server to send indications.
                // We receive them in the ValueChanged event handler.
                // Note that this sample configures either Indicate or Notify, but not both.
                var result = await
                        characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.Indicate);
                if (result == GattCommunicationStatus.Success)
                {
                    Debug.WriteLine("Successfully registered for indications");
                    IsIndicateSet = true;
                    return true;
                }
                else if (result == GattCommunicationStatus.ProtocolError)
                {
                    Debug.WriteLine("Error registering for indications: Protocol Error");
                    IsIndicateSet = false;
                    return false;
                }
                else if (result == GattCommunicationStatus.Unreachable)
                {
                    Debug.WriteLine("Error registering for indications: Unreachable");
                    IsIndicateSet = false;
                    return false;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // This usually happens when a device reports that it support indicate, but it actually doesn't.
                Debug.WriteLine("Unauthorized Exception: " + ex.Message);
                IsIndicateSet = false;
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Generic Exception: " + ex.Message);
                IsIndicateSet = false;
                return false;
            }

            IsIndicateSet = false;
            return false;
        }

        /// <summary>
        /// Unsets the indicate descriptor
        /// </summary>
        /// <returns>Unset indicate task</returns>
        public async Task<bool> StopIndicate()
        {
            if (IsIndicateSet == false)
            {
                // indicate is not set, can skip this
                return true;
            }

            try
            {
                // BT_Code: Must write the CCCD in order for server to send indications.
                // We receive them in the ValueChanged event handler.
                // Note that this sample configures either Indicate or Notify, but not both.
                var result = await
                        characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None);
                if (result == GattCommunicationStatus.Success)
                {
                    Debug.WriteLine("Successfully un-registered for indications");
                    IsIndicateSet = false;
                    return true;
                }
                else if (result == GattCommunicationStatus.ProtocolError)
                {
                    Debug.WriteLine("Error un-registering for indications: Protocol Error");
                    IsIndicateSet = true;
                    return false;
                }
                else if (result == GattCommunicationStatus.Unreachable)
                {
                    Debug.WriteLine("Error un-registering for indications: Unreachable");
                    IsIndicateSet = true;
                    return false;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // This usually happens when a device reports that it support indicate, but it actually doesn't.
                Debug.WriteLine("Exception: " + ex.Message);
                IsIndicateSet = true;
                return false;
            }

            return false;
        }

        private bool SocketAlready = false; // *MOD* - Added flag to check if a TCPIP connection already exist

                /// <summary>
        /// Sets the notify characteristic
        /// </summary>
        /// <returns>Set notify task</returns>
        public async Task<bool> SetNotify()
        {
            if (IsNotifySet == true)
            {
                // already set
                return true;
            }

            try
            {
                // BT_Code: Must write the CCCD in order for server to send indications.
                // We receive them in the ValueChanged event handler.
                // Note that this sample configures either Indicate or Notify, but not both.
                var result = await
                        characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.Notify);
                if (result == GattCommunicationStatus.Success)
                {
                    if (!SocketAlready) // *MOD* - On service Notified, Open new TCPIP socket if there is none existing, else skip 
                    {
                        Connect();
                    }

                    Debug.WriteLine("Successfully registered for notifications");
                    IsNotifySet = true;
                    return true;
                }
                else if (result == GattCommunicationStatus.ProtocolError)
                {
                    Debug.WriteLine("Error registering for notifications: Protocol Error");
                    IsNotifySet = false;
                    return false;
                }
                else if (result == GattCommunicationStatus.Unreachable)
                {
                    Debug.WriteLine("Error registering for notifications: Unreachable");
                    IsNotifySet = false;
                    return false;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // This usually happens when a device reports that it support indicate, but it actually doesn't.
                Debug.WriteLine("Unauthorized Exception: " + ex.Message);
                IsNotifySet = false;
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Generic Exception: " + ex.Message);
                IsNotifySet = false;
                return false;
            }

            IsNotifySet = false;
            return false;
        }

        /// <summary>
        /// Unsets the notify descriptor
        /// </summary>
        /// <returns>Unset notify task</returns>
        public async Task<bool> StopNotify()
        {
            if (IsNotifySet == false)
            {
                // indicate is not set, can skip this
                return true;
            }

            try
            {
                // BT_Code: Must write the CCCD in order for server to send indications.
                // We receive them in the ValueChanged event handler.
                // Note that this sample configures either Indicate or Notify, but not both.
                var result = await
                        characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None);
                if (result == GattCommunicationStatus.Success)
                {
                    Debug.WriteLine("Successfully un-registered for notifications");
                    IsNotifySet = false;

                    await Task.Delay(500).ContinueWith(t => Close(SocketAlready)); // *MOD* - Close the TCPIP socket as soon as service is denotified
                    SocketAlready = false; // *MOD* - Change flag for TCPIP socket 

                    return true;
                }
                else if (result == GattCommunicationStatus.ProtocolError)
                {
                    Debug.WriteLine("Error un-registering for notifications: Protocol Error");
                    IsNotifySet = true;
                    return false;
                }
                else if (result == GattCommunicationStatus.Unreachable)
                {
                    Debug.WriteLine("Error un-registering for notifications: Unreachable");
                    IsNotifySet = true;
                    return false;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // This usually happens when a device reports that it support indicate, but it actually doesn't.
                Debug.WriteLine("Exception: " + ex.Message);
                IsNotifySet = true;
                return false;
            }

            return false;
        }

        /// <summary>
        /// Executes when value changes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                () =>
            {
                SetValue(args.CharacteristicValue);
            });
        }

        /// <summary>
        /// helper function that copies the raw data into byte array
        /// </summary>
        /// <param name="buffer">The raw input buffer</param>
        private void SetValue(IBuffer buffer)
        {
            rawData = buffer;
            CryptographicBuffer.CopyToByteArray(rawData, out data);

            SetValue();
        }

        /// <summary>
        /// Sets the value of this characteristic based on the display type
        /// </summary>
        private void SetValue()
        {
            if (data == null)
            {
                Value = "NULL";
                return;
            }

            GattPresentationFormat format = null;

            if (characteristic.PresentationFormats.Count > 0)
            {
                format = characteristic.PresentationFormats[0];
            }

            // Determine what to set our DisplayType to
            if (format == null && DisplayType == DisplayTypes.NotSet)
            {
                if (name == "DeviceName")
                {
                    // All devices have DeviceName so this is a special case.
                    DisplayType = DisplayTypes.UTF8;
                }
                else
                {
                    string buffer = string.Empty;
                    bool isString = true;

                    try
                    {
                       buffer = GattConvert.ToUTF8String(rawData);
                    }
                    catch(Exception)
                    {
                        isString = false;
                    }

                    if (isString == true)
                    {

                        // if buffer is only 1 char or 2 char with 0 at end then let's assume it's hex
                        if (buffer.Length == 1)
                        {
                            isString = false;
                        }
                        else if (buffer.Length == 2 && buffer[1] == 0)
                        {
                            isString = false;
                        }
                        else
                        {
                            foreach (char b in buffer)
                            {
                                // if within the reasonable range of used characters and not null, let's assume it's a UTF8 string by default, else hex
                                if ((b < ' ' || b > '~') && b != 0)
                                {
                                    isString = false;
                                    break;
                                }
                            }
                        }
                    }

                    if (isString)
                    {
                        DisplayType = DisplayTypes.UTF8;
                    }
                    else
                    {
                        // By default, display as Hex
                        DisplayType = DisplayTypes.Hex;
                    }
                }
            }
            else if (format != null && DisplayType == DisplayTypes.NotSet)
            {
                if (format.FormatType == GattPresentationFormatTypes.Boolean ||
                    format.FormatType == GattPresentationFormatTypes.Bit2 ||
                    format.FormatType == GattPresentationFormatTypes.Nibble ||
                    format.FormatType == GattPresentationFormatTypes.UInt8 ||
                    format.FormatType == GattPresentationFormatTypes.UInt12 ||
                    format.FormatType == GattPresentationFormatTypes.UInt16 ||
                    format.FormatType == GattPresentationFormatTypes.UInt24 ||
                    format.FormatType == GattPresentationFormatTypes.UInt32 ||
                    format.FormatType == GattPresentationFormatTypes.UInt48 ||
                    format.FormatType == GattPresentationFormatTypes.UInt64 ||
                    format.FormatType == GattPresentationFormatTypes.SInt8 ||
                    format.FormatType == GattPresentationFormatTypes.SInt12 ||
                    format.FormatType == GattPresentationFormatTypes.SInt16 ||
                    format.FormatType == GattPresentationFormatTypes.SInt24 ||
                    format.FormatType == GattPresentationFormatTypes.SInt32)
                {
                    DisplayType = DisplayTypes.Decimal;
                }
                else if (format.FormatType == GattPresentationFormatTypes.Utf8)
                {
                    DisplayType = DisplayTypes.UTF8;
                }
                else if (format.FormatType == GattPresentationFormatTypes.Utf16)
                {
                    DisplayType = DisplayTypes.UTF16;
                }
                else if (format.FormatType == GattPresentationFormatTypes.UInt128 ||
                    format.FormatType == GattPresentationFormatTypes.SInt128 ||
                    format.FormatType == GattPresentationFormatTypes.DUInt16 ||
                    format.FormatType == GattPresentationFormatTypes.SInt64 ||
                    format.FormatType == GattPresentationFormatTypes.Struct ||
                    format.FormatType == GattPresentationFormatTypes.Float ||
                    format.FormatType == GattPresentationFormatTypes.Float32 ||
                    format.FormatType == GattPresentationFormatTypes.Float64)
                {
                    DisplayType = DisplayTypes.Unsupported;
                }
                else
                {
                    DisplayType = DisplayTypes.Unsupported;
                }
            }

            // Decode the value into the right display type
            if (DisplayType == DisplayTypes.Hex || DisplayType == DisplayTypes.Unsupported)
            {
                try
                {
                    Value = GattConvert.ToHexString(rawData);
                    string timestamp = DateTime.UtcNow.ToString("mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
                    Debug.WriteLine(timestamp + ":" + Value);
                    
                }
                catch(Exception)
                {
                    Value = "Error: Invalid hex value";
                }
            }
            else if (DisplayType == DisplayTypes.Decimal)
            {
                try
                {
                    Value = GattConvert.ToInt64(rawData).ToString();
                }
                catch(Exception)
                {
                    Value = "Error: Invalid Int64 Value";
                }
            }
            else if (DisplayType == DisplayTypes.UTF8)
            {
                try
                {
                    Value = GattConvert.ToUTF8String(rawData);
                }
                catch(Exception)
                {
                    Value = "Error: Invalid UTF8 String";
                }
            }
            else if (DisplayType == DisplayTypes.UTF16)
            {
                try
                {
                    Value = GattConvert.ToUTF16String(rawData);
                }
                catch(Exception)
                {
                    Value = "Error: Invalid UTF16 String";
                }
            }
            else if (DisplayType == DisplayTypes.Stream)
            {


                try
                {

                    //Value = rawData;
                    //Debug.WriteLine(UUID);

                    DataReader dataReader = DataReader.FromBuffer(rawData);
                    byte[] bytes1 = new byte[rawData.Length];

                    dataReader.ReadBytes(bytes1);

                    string rawString = BitConverter.ToString(bytes1, 0);
                    string timestamp = DateTime.UtcNow.ToString("mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
                    Debug.WriteLine(timestamp + ":" + rawString);


                    for(int i=0; i<bytes1.Length; i=i+2){

                        byte[] p = new byte[2];
                    Array.Copy(bytes1, i, p, 0, 2);
                    // Debug.WriteLine(BitConverter.ToString(p,0));
                    Array.Reverse(p);
                    int value = BitConverter.ToUInt16(p,0);
                    qt.Enqueue(value);
                    //Debug.WriteLine(qt.Count);
                    

                    }
                    //PrintValues(qt);

                    // byte[] p1 = new byte[4];
                    // byte[] p2 = new byte[4];
                    // byte[] p3 = new byte[4];
                    // byte[] p4 = new byte[4];
                    // byte[] p5 = new byte[4];
                    // byte[] p6 = new byte[4];
                    // byte[] p7 = new byte[4];
                    // byte[] p8 = new byte[4];
                    // byte[] p9 = new byte[4];
                    // byte[] p10 = new byte[4];

                    // byte[] rp1 = new byte[4];
                    // byte[] rp2 = new byte[4];
                    // byte[] rp3 = new byte[4];
                    // byte[] rp4 = new byte[4];
                    // byte[] rp5 = new byte[4];
                    // byte[] rp6 = new byte[4];
                    // byte[] rp7 = new byte[4];
                    // byte[] rp8 = new byte[4];
                    // byte[] rp9 = new byte[4];
                    // byte[] rp10 = new byte[4];

                    // Array.Copy(bytes1, 0, p1, 0, 3);
                    // Array.Copy(bytes1, 3, p2, 0, 3);
                    // Array.Copy(bytes1, 6, p3, 0, 3);
                    // Array.Copy(bytes1, 9, p4, 0, 3);
                    // Array.Copy(bytes1, 12, p5, 0, 3);
                    // Array.Copy(bytes1, 15, p6, 0, 3);
                    // Array.Copy(bytes1, 18, p7, 0, 3);
                    // Array.Copy(bytes1, 21, p8, 0, 3);
                    // Array.Copy(bytes1, 24, p9, 0, 3);
                    // Array.Copy(bytes1, 27, p10, 0, 3);

                    // Array.Reverse(p1);
                    // Array.Reverse(p2);
                    // Array.Reverse(p3);
                    // Array.Reverse(p4);
                    // Array.Reverse(p5);
                    // Array.Reverse(p6);
                    // Array.Reverse(p7);
                    // Array.Reverse(p8);
                    // Array.Reverse(p9);
                    // Array.Reverse(p10);

                    // Array.Copy(p1, rp1, 4);
                    // Array.Copy(p2, rp2, 4);
                    // Array.Copy(p3, rp3, 4);
                    // Array.Copy(p4, rp4, 4);
                    // Array.Copy(p5, rp5, 4);
                    // Array.Copy(p6, rp6, 4);
                    // Array.Copy(p7, rp7, 4);
                    // Array.Copy(p8, rp8, 4);
                    // Array.Copy(p9, rp9, 4);
                    // Array.Copy(p10, rp10, 4);

                    // uint k1 = BitConverter.ToUInt32(rp1, 0);
                    // int m1 = (int)k1;
                    // m1 = (int)(m1 >> 8);

                    // uint k2 = BitConverter.ToUInt32(rp2, 0);
                    // int m2 = (int)k2;
                    // m2 = (int)(m2 >> 8);

                    // uint k3 = BitConverter.ToUInt32(rp3, 0);
                    // int m3 = (int)k3;
                    // m3 = (int)(m3 >> 8);

                    // uint k4 = BitConverter.ToUInt32(rp4, 0);
                    // int m4 = (int)k4;
                    // m4 = (int)(m4 >> 8);

                    // uint k5 = BitConverter.ToUInt32(rp5, 0);
                    // int m5 = (int)k5;
                    // m5 = (int)(m5 >> 8);

                    // uint k6 = BitConverter.ToUInt32(rp6, 0);
                    // int m6 = (int)k6;
                    // m6 = (int)(m6 >> 8);

                    // uint k7 = BitConverter.ToUInt32(rp7, 0);
                    // int m7 = (int)k7;
                    // m7 = (int)(m7 >> 8);

                    // uint k8 = BitConverter.ToUInt32(rp8, 0);
                    // int m8 = (int)k8;
                    // m8 = (int)(m8 >> 8);

                    // uint k9 = BitConverter.ToUInt32(rp9, 0);
                    // int m9 = (int)k9;
                    // m9 = (int)(m9 >> 8);

                    // uint k10 = BitConverter.ToUInt32(rp10, 0);
                    // int m10 = (int)k10;
                    // m10 = (int)(m10 >> 8);

                    /*
                    Value = GattConvert.ToHexString(rawData);
                    String v1 = Value.Substring(0, 2);
                    String v2 = Value.Substring(3, 2);
                    String v3 = Value.Substring(6, 2);
                    String v4 = Value.Substring(9, 2);
                    String v5 = Value.Substring(12, 2);
                    String v6 = Value.Substring(15, 2);
                    String v7 = Value.Substring(18, 2);
                    String v8 = Value.Substring(21, 2);
                    String v9 = Value.Substring(24, 2);
                    String v10 = Value.Substring(27, 2);
                    String v11 = Value.Substring(30, 2);
                    String v12 = Value.Substring(33, 2);
                    String v13 = Value.Substring(36, 2);
                    String v14 = Value.Substring(39, 2);
                    String v15 = Value.Substring(42, 2);
                    String v16 = Value.Substring(45, 2);
                    String v17 = Value.Substring(48, 2);
                    String v18 = Value.Substring(51, 2);
                    String v19 = Value.Substring(54, 2);
                    String v20 = Value.Substring(57, 2);
                    String v21 = Value.Substring(60, 2);
                    String v22 = Value.Substring(63, 2);
                    String v23 = Value.Substring(66, 2);
                    String v24 = Value.Substring(69, 2);
                    String v25 = Value.Substring(72, 2);
                    String v26 = Value.Substring(75, 2);
                    String v27 = Value.Substring(78, 2);
                    String v28 = Value.Substring(81, 2);
                    String v29 = Value.Substring(84, 2);
                    String v30 = Value.Substring(87, 2);

                    
                    String S1 = String.Concat(v1 + v2 + v3);
                    String S2 = String.Concat(v4 + v5 + v6);
                    String S3 = String.Concat(v7 + v8 + v9);
                    String S4 = String.Concat(v10 + v11 + v12);
                    String S5 = String.Concat(v13 + v14 + v15);
                    String S6 = String.Concat(v16 + v17 + v18);
                    String S7 = String.Concat(v19 + v20 + v21);
                    String S8 = String.Concat(v22 + v23 + v24);
                    String S9 = String.Concat(v25 + v26 + v27);
                    String S10 = String.Concat(v28 + v29 + v30);
                    
                    /*
                    String S1 = String.Concat(v3 + v2 + v1);
                    String S2 = String.Concat(v6 + v5 + v4);
                    String S3 = String.Concat(v9 + v8 + v7);
                    String S4 = String.Concat(v12 + v11 + v10);
                    String S5 = String.Concat(v15 + v14 + v13);
                    String S6 = String.Concat(v18 + v17 + v16);
                    String S7 = String.Concat(v21 + v20 + v19);
                    String S8 = String.Concat(v24 + v23 + v22);
                    String S9 = String.Concat(v27 + v26 + v25);
                    String S10 = String.Concat(v30 + v29 + v28);
                    */
                    /*
                    long n1 = Int64.Parse(S1, System.Globalization.NumberStyles.HexNumber);
                    long n2 = Int64.Parse(S2, System.Globalization.NumberStyles.HexNumber);
                    long n3 = Int64.Parse(S3, System.Globalization.NumberStyles.HexNumber);
                    long n4 = Int64.Parse(S4, System.Globalization.NumberStyles.HexNumber);
                    long n5 = Int64.Parse(S5, System.Globalization.NumberStyles.HexNumber);
                    long n6 = Int64.Parse(S6, System.Globalization.NumberStyles.HexNumber);
                    long n7 = Int64.Parse(S7, System.Globalization.NumberStyles.HexNumber);
                    long n8 = Int64.Parse(S8, System.Globalization.NumberStyles.HexNumber);
                    long n9 = Int64.Parse(S9, System.Globalization.NumberStyles.HexNumber);
                    long n10 = Int64.Parse(S10, System.Globalization.NumberStyles.HexNumber);
                    */
                    // string timestamp = DateTime.UtcNow.ToString("mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

                    //Value = (timestamp + ":" + n1.ToString() + "," + n2.ToString() + "," + n3.ToString() + "," + n4.ToString() + "," + n5.ToString() + "," + n6.ToString() + "," + n7.ToString() + "," + n8.ToString() + "," + n9.ToString() + "," + n10.ToString());


                    // Value = (timestamp + ":" + m1.ToString() + "," + m2.ToString() + "," + m3.ToString() + "," + m4.ToString() + "," + m5.ToString() + "," + m6.ToString() + "," + m7.ToString() + "," + m8.ToString() + "," + m9.ToString() + "," + m10.ToString());
                    // Debug.WriteLine(Value);

                    /*qt.Enqueue(n1);
                    qt.Enqueue(n2);
                    qt.Enqueue(n3);
                    qt.Enqueue(n4);
                    qt.Enqueue(n5);
                    qt.Enqueue(n6);
                    qt.Enqueue(n7);
                    qt.Enqueue(n8);
                    qt.Enqueue(n9);
                    qt.Enqueue(n10);*/

                    // qt.Enqueue(m1);
                    // qt.Enqueue(m2);
                    // qt.Enqueue(m3);
                    // qt.Enqueue(m4);
                    // qt.Enqueue(m5);
                    // qt.Enqueue(m6);
                    // qt.Enqueue(m7);
                    // qt.Enqueue(m8);
                    // qt.Enqueue(m9);
                    // qt.Enqueue(m10);

                    //if (SocketAlready)
                    //{ Send("100,200,300");
                    //    Send("\n"); }

                }
                catch (Exception)
                {
                    Value = "Error: Invalid CUSTOM String";
                }




                // try
                // {
                //     //Value = GattConvert.ToInt64(rawData).ToString();
                //     Value = GattConvert.ToHexString(rawData);
                //     Debug.WriteLine(Value);

                //   if (SocketAlready)
                //    {
                //         Send(Value + "\n"); // *MOD* Send data as soon as recieved from BLE device
                //     }

                //     // *MOD* optionaly you can add value to queue and send the data at a desired interval using timer
                //     // qt.Enqueue(Value); // *MOD*uncomment this to add value to queue
                // }
                // catch (Exception)
                // {
                //     Value = "Error: Invalid Custom String";
                // }
            }
        }


        private StreamSocket _socket;
        private DataWriter _writer;
        public delegate void Error(string message);
       
        public string Ip = "127.0.0.1"; // *MOD* - Localhost or your desired server IP
        public int Port = 12345; // *MOD* - Your desired port

        public async void Connect() // *MOD* This method connects to a server listenting for connection 
        {
            try
            {
                var hostName = new HostName(Ip);
                _socket = new StreamSocket();
                await _socket.ConnectAsync(hostName, Port.ToString());
                _writer = new DataWriter(_socket.OutputStream);
                
                SocketAlready = true; // *MOD* - Once TCPIP socket is established change the flag
                await Task.Delay(2000).ContinueWith(t => StopWatch());  // *MOD* -Comment this line you dont want data to be send at custom timed interval

            }
            catch (Exception ex)
            {

                Debug.WriteLine(ex.ToString());
                SocketAlready = false;

            }
        }

        public async void Send(string message) // *MOD* - This method sends your data over TCPIP
        {


            Byte[] encodedBytes = Encoding.ASCII.GetBytes(message);

            _writer.WriteBytes(encodedBytes);

            try
            {
                await _writer.StoreAsync();
                await _writer.FlushAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        public void Close(bool socketStatus) // *MOD*  - This method close the existing TCPIP connection
        {
            if (socketStatus)
            {
                _writer.DetachStream();
                _writer.Dispose();
                _socket.Dispose();
            }
            
        }

           
        
        ThreadPoolTimer _atimer = null;

        public void StopWatch() // *MOD* - This is an optonal timer in case you want to stream incoming data at a regular interval
        {
            _atimer = ThreadPoolTimer.CreatePeriodicTimer(_clockTimer_Tick, TimeSpan.FromMilliseconds(8)); // *MOD* Set time here 

        }


        private void _clockTimer_Tick(ThreadPoolTimer timer)
        {

            // *MOD* - Check if queue onot empty and not null, then perform operation specific to Characterestic UUID
            
            if (qt.Count != 0)
            {

                String first = qt.Peek()?.ToString();
                if (first != null)
                {

                    // if (UUID == "00004a37-0000-1000-8000-00805f9b34fb")
                    // {
                        int yourData1; 
                        int.TryParse(first, out yourData1);
                        qt.Dequeue();

                        //yourData2  = 2 * YourData1; // Your desired operations
            
                        Send(yourData1 + "\n"); // *MOD* send your data stream seperated by "," and ending with \n
                    // }
                }
            }
            
            

            if (!SocketAlready)  // *MOD* - Close timer as when the service is denotified
            {
                _atimer.Cancel();
                
            }
        }

        public static void PrintValues( IEnumerable myCollection )  {
      foreach ( Object obj in myCollection )
         Console.Write( " {0}", obj );
      Console.WriteLine();
   }


        /// <summary>
        /// Event to notify when this object has changed
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Executes when this class changes
        /// </summary>
        /// <param name="e"></param>
        private void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "DisplayType")
            {
                Debug.WriteLine($"{this.Name} - DisplayType set: {this.DisplayType.ToString()}");
            }

            if (PropertyChanged != null)
            {
                PropertyChanged(this, e);
            }
        }
    }
}