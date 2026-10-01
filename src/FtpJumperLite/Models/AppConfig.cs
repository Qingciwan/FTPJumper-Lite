using System.Collections.Generic;
using Newtonsoft.Json;

namespace FtpJumperLite.Models
{
    /// <summary>subjects.json 的根结构。</summary>
    public sealed class AppConfig
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 2;

        [JsonProperty("subjects")]
        public List<Subject> Subjects { get; set; } = new List<Subject>();
    }
}
