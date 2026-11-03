using CDTag.Common.Mvvm;

namespace CDTag.ViewModels.Checksum
{
    public interface IChecksumViewModel : IViewModelBase
    {
        /// <summary>Opens <paramref name="folder"/> to create a checksum file, or to verify one (<paramref name="checksumFile"/>, or the first found).</summary>
        void Load(string folder, bool verify, string checksumFile = null);
    }
}
