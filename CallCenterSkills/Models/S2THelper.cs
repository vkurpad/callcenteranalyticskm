using System;
using System.Collections.Generic;
using System.Text;

namespace CallCenterSkills.Models
{
    public class Transcription
    {
        public string DisplayName { get; set; }
        public Project project { get; set; }
        public IEnumerable<Uri> ContentUrls { get; set; }
        public Model model { get; set; }
        public string Locale { get; set; }
        public string name { get; set; }
        public string Description { get; set; }
        public Properties properties { get; set; }
        public Uri ContentContainerUrl { get; set; }
        public DateTime CreatedDateTime { get; set; }
        public DateTime LastActionDateTime { get; set; }
        public string Status { get; set; }

       
    }

    public class Project
    {
        public string id { get; set; }
    }

    public class Properties
    {
        public bool wordLevelTimestampsEnabled { get; set; }
        public bool diarizationEnabled { get; set; }
        public string ProfanityFilterMode { get; set; }
        public string PunctuationMode { get; set; }
        public string destinationContainerUrl { get; set; }
    }

    public class Model
    {
        public string id { get; set; }
    }
}