using CDTag.Common.Mvvm;

namespace CDTag.ViewModels.Checksum
{
    public interface IVerifyEACLogViewModel : IViewModelBase
    {
        /// <summary>Lists the .log files in <paramref name="folder"/> (and its disc folders) and shows <paramref name="logFile"/>, or the first one.</summary>
        void Load(string folder, string logFile = null);
    }
}
