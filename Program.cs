using CSCore.CoreAudioAPI;
using Microsoft.Win32;
using System.Text.Json;

namespace WASAPI_Audio_Mirror
{
    internal static class Program
    {

        private static NotifyIcon trayIcon = null;
        private static ContextMenuStrip trayMenu = null;
        private static ToolStripMenuItem inputItem = null;
        private static ToolStripMenuItem outputItem = null;
        private static ToolStripMenuItem pauseItem = null;
        private static ToolStripMenuItem restartItem = null;
        private static ToolStripMenuItem exitItem = null;

        private static CancellationTokenSource cts = new CancellationTokenSource();
        private static ConcurrentExclusiveSchedulerPair scheduler = new ConcurrentExclusiveSchedulerPair();
        private static TaskFactory exclusiveFactory = new TaskFactory(scheduler.ExclusiveScheduler);

        private static volatile bool paused = false;
        private static volatile bool updateDevices = false;
        private static MMDeviceEnumerator deviceEnumerator = new MMDeviceEnumerator();
        private static MMNotificationClient deviceNotification = new MMNotificationClient(deviceEnumerator);

        private static Settings settings = new Settings();
        private static AudioMirror audioMirror = new AudioMirror(settings, deviceEnumerator, cts.Token);

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            readSettings();
            trayMenu = new ContextMenuStrip();
            trayMenu.Opening += (s, e) => { if (updateDevices) { updateDevices = false; findAudioDevices(); }; };
            inputItem = new ToolStripMenuItem("Input Audio Device");
            inputItem.Name = "Input Audio Device";
            trayMenu.Items.Add(inputItem);
            outputItem = new ToolStripMenuItem("Output Audio Device");
            outputItem.Name = "Output Audio Device";
            findAudioDevices();
            if (checkReady())
            {
                exclusiveFactory.StartNew(() => audioMirror.startMirror());
                exclusiveFactory.StartNew(() => audioMirror.updateOutputs());
            }
            deviceNotification.DeviceAdded += (s, e) => { updateDevices = true; exclusiveFactory.StartNew(() => audioMirror.updateOutputs()); };
            deviceNotification.DeviceRemoved += (s, e) => { updateDevices = true; exclusiveFactory.StartNew(() => audioMirror.updateOutputs()); };
            deviceNotification.DeviceStateChanged += (s, e) => { updateDevices = true; exclusiveFactory.StartNew(() => audioMirror.updateOutputs()); };
            deviceNotification.DevicePropertyChanged += (s, e) => { updateDevices = true; exclusiveFactory.StartNew(() => audioMirror.updateOutputs()); };
            trayMenu.Items.Add(outputItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            pauseItem = new ToolStripMenuItem("Pause");
            pauseItem.Click += new EventHandler(onPause);
            pauseItem.Name = "Pause";
            trayMenu.Items.Add(pauseItem);
            restartItem = new ToolStripMenuItem("Restart Mirror");
            restartItem.Click += new EventHandler(onRestart);
            restartItem.Name = "Restart Mirror";
            trayMenu.Items.Add(restartItem);
            exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += new EventHandler(onExit);
            exitItem.Name = "Exit";
            trayMenu.Items.Add(exitItem);
            trayIcon = new NotifyIcon();
            trayIcon.Text = "WASAPI-Audio-Mirror";
            trayIcon.Icon = Properties.Resources.icon;
            trayIcon.Visible = true;
            trayIcon.ContextMenuStrip = trayMenu;

            SystemEvents.PowerModeChanged += onPowerModeChange;

            Application.ApplicationExit += new EventHandler(onExit);
            Application.Run();
        }

        private static bool checkReady()
        {
            int tries = 0;
            bool foundInput = false;
            bool foundOutput = false;
            while (true)
            {
                tries++;
                try
                {
                    MMDevice device = deviceEnumerator.GetDevice(settings.selectedInputDeviceId);
                    if (device.DeviceState == DeviceState.Active)
                    {
                        foundInput = true;
                        break;
                    }
                }
                catch (Exception) { }
                if (tries >= 10)
                {
                    break;
                }
                Thread.Sleep(5000);
            }
            if (foundInput)
            {
                tries = 0;
                while (true)
                {
                    tries++;
                    foreach (string id in settings.selectedOutputDeviceIds)
                    {
                        try
                        {
                            MMDevice device = deviceEnumerator.GetDevice(id);
                            if (device.DeviceState == DeviceState.Active)
                            {
                                foundOutput = true;
                                break;
                            }
                        }
                        catch (Exception) { }
                    }
                    if (foundOutput || tries >= 10)
                    {
                        break;
                    }
                    Thread.Sleep(5000);
                }
                if (foundOutput)
                {
                    return true;
                }
            }
            return false;
        }

        private static void onPowerModeChange(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                System.Diagnostics.Debug.WriteLine("Detecting system resume from sleep, restarting mirror");
                exclusiveFactory.StartNew(() => audioMirror.stopMirror());
                if (checkReady())
                {
                    exclusiveFactory.StartNew(() => audioMirror.startMirror());
                    exclusiveFactory.StartNew(() => audioMirror.updateOutputs());
                }
            }
        }

        private static void onPause(object? sender, EventArgs e)
        {
            if (paused)
            {
                System.Diagnostics.Debug.WriteLine("Resuming mirror");
                paused = false;
                pauseItem.Name = "Pause";
                pauseItem.Text = "Pause";
                pauseItem.CheckState = CheckState.Unchecked;
                exclusiveFactory.StartNew(() => audioMirror.resumeMirror());
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Pausing mirror");
                paused = true;
                pauseItem.Name = "Resume";
                pauseItem.Text = "Resume";
                pauseItem.CheckState = CheckState.Checked;
                exclusiveFactory.StartNew(() => audioMirror.pauseMirror());
            }
        }

        private static void onRestart(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Restarting mirror");
            exclusiveFactory.StartNew(() => audioMirror.stopMirror());
            exclusiveFactory.StartNew(() => audioMirror.startMirror());
            exclusiveFactory.StartNew(() => audioMirror.updateOutputs());
        }

        private static void onExit(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Shutting down!");
            deviceNotification.Dispose();
            cts.Cancel();
            Application.Exit();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
        }

        private static void selectDevice(object? sender, EventArgs e, DeviceType type, string deviceId, ToolStripMenuItem item)
        {
            if (type == DeviceType.INPUT)
            {
                if (settings.selectedInputDeviceId != null && settings.selectedInputDeviceId.Equals(deviceId))
                {
                    System.Diagnostics.Debug.WriteLine("Input " + item.Text + " already selected, unselecting");
                    settings.selectedInputDeviceId = null;
                    if (settings.selectedInputDeviceItem != null)
                    {
                        settings.selectedInputDeviceItem.CheckState = CheckState.Unchecked;
                    }
                    settings.selectedInputDeviceItem = null;
                    exclusiveFactory.StartNew(() => audioMirror.stopMirror());
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Input " + item.Text + " selected");
                    settings.selectedInputDeviceId = deviceId;
                    if (settings.selectedInputDeviceItem != null)
                    {
                        System.Diagnostics.Debug.WriteLine("Input " + settings.selectedInputDeviceItem.Name + " unselected");
                        settings.selectedInputDeviceItem.CheckState = CheckState.Unchecked;
                    }
                    settings.selectedInputDeviceItem = item;
                    item.CheckState = CheckState.Checked;
                    if (settings.selectedOutputDeviceIds.Contains(deviceId))
                    {
                        System.Diagnostics.Debug.WriteLine("Output " + item.Text + " already selected, unselecting");
                        settings.selectedOutputDeviceIds.Remove(deviceId);
                        settings.selectedOutputDeviceItems.RemoveWhere(item => { if (item.Name.Equals(deviceId)) { item.CheckState = CheckState.Unchecked; return true; } return false; });
                    }
                    exclusiveFactory.StartNew(() => audioMirror.stopMirror());
                    exclusiveFactory.StartNew(() => audioMirror.startMirror());
                    exclusiveFactory.StartNew(() => audioMirror.updateOutputs());
                }
            }
            else
            {
                if (settings.selectedOutputDeviceIds.Contains(deviceId))
                {
                    System.Diagnostics.Debug.WriteLine("Output " + item.Text + " already selected, unselecting");
                    settings.selectedOutputDeviceIds.Remove(deviceId);
                    settings.selectedOutputDeviceItems.RemoveWhere(item => { if (item.Name.Equals(deviceId)) { item.CheckState = CheckState.Unchecked; return true; } return false; });
                    if (settings.selectedOutputDeviceIds.Count > 0)
                    {
                        exclusiveFactory.StartNew(() => audioMirror.updateOutputs());
                    }
                    else
                    {
                        exclusiveFactory.StartNew(() => audioMirror.stopMirror());
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Output " + item.Text + " selected");
                    settings.selectedOutputDeviceIds.Add(deviceId);
                    settings.selectedOutputDeviceItems.Add(item);
                    item.CheckState = CheckState.Checked;
                    if (settings.selectedInputDeviceId != null && settings.selectedInputDeviceId.Equals(deviceId))
                    {
                        System.Diagnostics.Debug.WriteLine("Input " + item.Text + " already selected, unselecting");
                        settings.selectedInputDeviceId = null;
                        if (settings.selectedInputDeviceItem != null)
                        {
                            settings.selectedInputDeviceItem.CheckState = CheckState.Unchecked;
                        }
                        settings.selectedInputDeviceItem = null;
                        exclusiveFactory.StartNew(() => audioMirror.stopMirror());
                    }
                    else
                    {
                        exclusiveFactory.StartNew(() => audioMirror.startMirror());
                        exclusiveFactory.StartNew(() => audioMirror.updateOutputs());
                    }
                }
            }
            saveSettings();
        }

        private static void findAudioDevices()
        {
            settings.selectedOutputDeviceItems.Clear();
            inputItem.DropDownItems.Clear();
            outputItem.DropDownItems.Clear();
            settings.selectedOutputDeviceIds.Remove(settings.selectedInputDeviceId);
            foreach (MMDevice device in MMDeviceEnumerator.EnumerateDevices(DataFlow.Render, DeviceState.Active))
            {
                System.Diagnostics.Debug.WriteLine("Found device " + device.FriendlyName + " ID: " + device.DeviceID);
                ToolStripMenuItem itemInput = new ToolStripMenuItem();
                itemInput.Text = device.FriendlyName;
                itemInput.Name = device.DeviceID;
                itemInput.Click += new EventHandler((sender, EventArgs) => { selectDevice(sender, EventArgs, DeviceType.INPUT, device.DeviceID, itemInput); });
                ToolStripMenuItem itemOutput = new ToolStripMenuItem();
                itemOutput.Text = device.FriendlyName;
                itemOutput.Name = device.DeviceID;
                itemOutput.Click += new EventHandler((sender, EventArgs) => { selectDevice(sender, EventArgs, DeviceType.OUTPUT, device.DeviceID, itemOutput); });
                if (settings.selectedInputDeviceId != null)
                {
                    if (settings.selectedInputDeviceId.Equals(device.DeviceID))
                    {
                        System.Diagnostics.Debug.WriteLine("Found previously selected input device: " + device.FriendlyName);
                        itemInput.CheckState = CheckState.Checked;
                        settings.selectedInputDeviceItem = itemInput;
                    }
                }
                if (settings.selectedOutputDeviceIds.Contains(device.DeviceID))
                {
                    System.Diagnostics.Debug.WriteLine("Found previously selected output device: " + device.FriendlyName);
                    itemOutput.CheckState = CheckState.Checked;
                    settings.selectedOutputDeviceItems.Add(itemOutput);
                }
                inputItem.DropDownItems.Add(itemInput);
                outputItem.DropDownItems.Add(itemOutput);
            }
        }

        private static void saveSettings()
        {
            try
            {
                using (Stream stream = File.Open("audio-mirror.json", FileMode.Create))
                {
                    JsonSerializer.Serialize(stream, settings);
                }
            }
            catch (Exception) { }
        }

        private static void readSettings()
        {
            try
            {
                using (Stream stream = File.Open("audio-mirror.json", FileMode.Open))
                {
                    settings = JsonSerializer.Deserialize<Settings>(stream);
                    if (settings == null)
                    {
                        settings = new Settings();
                    }
                    audioMirror.updateSettings(settings);
                }
            }
            catch (Exception) { }
        }

        private enum DeviceType
        {
            INPUT,
            OUTPUT
        }
    }
}