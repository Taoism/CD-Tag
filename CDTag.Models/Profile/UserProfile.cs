using System;
using System.ComponentModel;
using System.Xml.Serialization;
using CDTag.Common;
using CDTag.Common.Model;

namespace CDTag.Model.Profile
{
    public partial class UserProfile : ModelBase<UserProfile>
    {
        public UserProfile()
        {
            Revision = 1;
            FileNaming = new FileNaming();
            NFOOptions = new NFOOptions();
            Finish = new Finish();
            LastModified = DateTime.Now;

            PropertyChanged += (s, e) => CheckHasChanges();
            SubPropertyChanged += (s, e) => CheckHasChanges();
        }

        [XmlIgnore]
        public string ProfileName
        {
            get { return Get<string>("ProfileName"); }
            set { Set("ProfileName", value); }
        }

        [XmlIgnore]
        public bool HasChanges
        {
            get { return Get<bool>("HasChanges"); }
            set { Set("HasChanges", value); }
        }

        [XmlIgnore]
        public string ModelHash
        {
            get { return Get<string>("ModelHash"); }
            set { Set("ModelHash", value); }
        }

        [XmlIgnore]
        public DateTime LastModified
        {
            get { return Get<DateTime>("LastModified"); }
            set { Set("LastModified", value); }
        }

        public int Revision
        {
            get { return Get<int>("Revision"); }
            set { Set("Revision", value); }
        }

        /// <summary>
        /// Audio file types this profile is meant for, as lower-case extensions without the dot,
        /// comma separated ("mp3", "flac", "mp3,flac"). Null or empty means any type.
        /// Tag Album uses it to pick the profile automatically (see <see cref="ProfileSelector"/>).
        /// </summary>
        public string AudioFileTypes
        {
            get { return Get<string>("AudioFileTypes"); }
            set { Set("AudioFileTypes", value); }
        }

        /// <summary>The profile name (used by lists and accessibility tools).</summary>
        public override string ToString()
        {
            return ProfileName ?? base.ToString();
        }

        public FileNaming FileNaming
        {
            get { return Get<FileNaming>("FileNaming"); }
            set { Set("FileNaming", value); }
        }

        public NFOOptions NFOOptions
        {
            get { return Get<NFOOptions>("NFOOptions"); }
            set { Set("NFOOptions", value); }
        }

        public Finish Finish
        {
            get { return Get<Finish>("Finish"); }
            set { Set("Finish", value); }
        }
    }
}
