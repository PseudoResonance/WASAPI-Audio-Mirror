using CSCore.CoreAudioAPI;
using CSCore.SoundIn;
using CSCore.SoundOut;
using CSCore.Streams;

namespace WASAPI_Audio_Mirror
{
    internal class AudioMirror
    {

        private Settings settings;
        private MMDeviceEnumerator enumerator;
        private CancellationToken token;

        private volatile bool started = false;
        private volatile bool paused = false;
        private volatile WasapiCapture capture = null;
        private volatile SoundInSource source = null;
        private HashSet<string> outputStrings = new HashSet<string>(1);
        private HashSet<WasapiOut> outputs = new HashSet<WasapiOut>(1);

        public AudioMirror(Settings settings, MMDeviceEnumerator deviceEnumerator, CancellationToken token)
        {
            this.settings = settings;
            this.enumerator = deviceEnumerator;
            this.token = token;
            token.Register(stopMirror);
        }

        public void updateSettings(Settings settings)
        {
            this.settings = settings;
        }

        public void startMirror()
        {
            if (!started && settings.selectedInputDeviceId != null)
            {
                try
                {
                    capture = new WasapiLoopbackCapture();
                    MMDevice device = enumerator.GetDevice(settings.selectedInputDeviceId);
                    if (device != null && device.DeviceState == DeviceState.Active)
                    {
                        System.Diagnostics.Debug.WriteLine("Starting audio mirror");
                        capture.Device = device;
                        capture.Initialize();
                        if (paused)
                        {
                            capture.Stop();
                        }
                        else
                        {
                            capture.Start();
                        }

                        source = new SoundInSource(capture) { FillWithZeros = true };
                        started = true;
                    }
                }
                catch (Exception) { }
            }
        }

        public void stopMirror()
        {
            System.Diagnostics.Debug.WriteLine("Stopping audio mirror");
            try
            {
                outputStrings.Clear();
                outputs.RemoveWhere(output =>
                {
                    output.Dispose();
                    return true;
                });
                if (source != null)
                {
                    source.Dispose();
                }
                if (capture != null)
                {
                    capture.Dispose();
                }
            }
            catch (Exception) { }
            started = false;
        }

        public void pauseMirror()
        {
            System.Diagnostics.Debug.WriteLine("Pausing audio mirror");
            paused = true;
            if (capture != null)
            {
                capture.Stop();
            }
        }

        public void resumeMirror()
        {
            System.Diagnostics.Debug.WriteLine("Resuming audio mirror");
            paused = false;
            if (capture != null)
            {
                capture.Start();
            }
        }

        public void updateOutputs()
        {
            if (started && capture != null && source != null && capture.Device.DeviceState == DeviceState.Active)
            {
                System.Diagnostics.Debug.WriteLine("Updating audio mirror");
                try
                {
                    outputs.RemoveWhere(output => {
                        if (output.Device.DeviceState != DeviceState.Active)
                        {
                            outputStrings.Remove(output.Device.DeviceID);
                            return true;
                        }
                        return false;
                    });
                    outputStrings.RemoveWhere(id =>
                    {
                        if (!settings.selectedOutputDeviceIds.Contains(id))
                        {
                            outputs.RemoveWhere(output =>
                            {
                                if (id.Equals(output.Device.DeviceID))
                                {
                                    output.Dispose();
                                    return true;
                                }
                                return false;
                            });
                            return true;
                        }
                        return false;
                    });
                    foreach (string id in settings.selectedOutputDeviceIds)
                    {
                        if (!outputStrings.Contains(id))
                        {
                            MMDevice device = enumerator.GetDevice(id);
                            if (device != null && device.DeviceState == DeviceState.Active)
                            {
                                WasapiOut output = new WasapiOut(true, AudioClientShareMode.Shared, 5);
                                output.Device = device;
                                output.Initialize(source);
                                output.Play();
                                outputs.Add(output);
                                outputStrings.Add(id);
                            }
                        }
                    }
                }
                catch (Exception) { }
            }
        }

    }
}
