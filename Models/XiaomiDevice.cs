namespace MiSound.AI.Local.Models
{
    public class XiaomiDevice
    {
        public string Name { get; set; }
        public string Model { get; set; }
        public string Did { get; set; }
        public string DeviceId { get; set; }
        public string Hardware { get; set; }
        public bool? IsOnline { get; set; }

        public override string ToString()
        {
            return Name + " | " + Model + " | DID=" + Did + " | deviceID=" + DeviceId + (IsOnline.HasValue ? " | Online=" + IsOnline.Value : "");
        }
    }
}
