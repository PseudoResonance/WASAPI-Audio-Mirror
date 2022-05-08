using System.Text.Json.Serialization;

namespace WASAPI_Audio_Mirror
{
    [Serializable()]
    internal class Settings
    {
        [JsonInclude]
        public volatile string selectedInputDeviceId = "";

        public volatile ToolStripMenuItem selectedInputDeviceItem = null;

        [JsonInclude]
        public HashSet<string> selectedOutputDeviceIds = new HashSet<string>(1);

        public HashSet<ToolStripMenuItem> selectedOutputDeviceItems = new HashSet<ToolStripMenuItem>(1);
    }
}
