using CDTag.Common;
using CDTag.Common.Model;

namespace CDTag.Model.Profile
{
    public class Finish : ModelBase<Finish>
    {
        public Finish()
        {
            RenameDirectory = true;
            RenameAudioFiles = true;
            RenameCUEFiles = true;
            RenameImageFiles = true;
            RenameEACLogFiles = true;
            CreatePlaylist = true;
            NFO = FinishNFO.RenameExisting;
            WriteCUEFilesToPlaylist = true;
            DeleteOldFiles = true;
            MoveOutOfDiscFolders = true;
        }

        public bool RenameDirectory
        {
            get { return Get<bool>(nameof(RenameDirectory)); }
            set { Set(nameof(RenameDirectory), value); }
        }

        public bool RenameAudioFiles
        {
            get { return Get<bool>(nameof(RenameAudioFiles)); }
            set { Set(nameof(RenameAudioFiles), value); }
        }

        public bool RenameCUEFiles
        {
            get { return Get<bool>(nameof(RenameCUEFiles)); }
            set { Set(nameof(RenameCUEFiles), value); }
        }

        public bool RenameImageFiles
        {
            get { return Get<bool>(nameof(RenameImageFiles)); }
            set { Set(nameof(RenameImageFiles), value); }
        }

        public bool RenameEACLogFiles
        {
            get { return Get<bool>(nameof(RenameEACLogFiles)); }
            set { Set(nameof(RenameEACLogFiles), value); }
        }

        public bool CreatePlaylist
        {
            get { return Get<bool>(nameof(CreatePlaylist)); }
            set { Set(nameof(CreatePlaylist), value); }
        }

        public bool WriteCUEFilesToPlaylist
        {
            get { return Get<bool>(nameof(WriteCUEFilesToPlaylist)); }
            set { Set(nameof(WriteCUEFilesToPlaylist), value); }
        }

        public bool CreateSFV
        {
            get { return Get<bool>(nameof(CreateSFV)); }
            set { Set(nameof(CreateSFV), value); }
        }

        public bool CreateMD5
        {
            get { return Get<bool>(nameof(CreateMD5)); }
            set { Set(nameof(CreateMD5), value); }
        }

        public bool CreateSHA1
        {
            get { return Get<bool>(nameof(CreateSHA1)); }
            set { Set(nameof(CreateSHA1), value); }
        }

        public FinishNFO NFO
        {
            get { return Get<FinishNFO>(nameof(NFO)); }
            set { Set(nameof(NFO), value); }
        }

        public bool DeleteOldFiles
        {
            get { return Get<bool>(nameof(DeleteOldFiles)); }
            set { Set(nameof(DeleteOldFiles), value); }
        }

        public bool DeleteEmptySubdirectories
        {
            get { return Get<bool>(nameof(DeleteEmptySubdirectories)); }
            set { Set(nameof(DeleteEmptySubdirectories), value); }
        }

        /// <summary>
        /// Multi-disc albums with CD1 / CD2 / ... folders: move every file into the album folder (the disc number
        /// goes into the names, 101, 201, ...) and delete the disc folders.
        /// </summary>
        public bool MoveOutOfDiscFolders
        {
            get { return Get<bool>(nameof(MoveOutOfDiscFolders)); }
            set { Set(nameof(MoveOutOfDiscFolders), value); }
        }
    }
}
