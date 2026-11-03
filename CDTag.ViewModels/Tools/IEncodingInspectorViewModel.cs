using System.Collections.Generic;
using CDTag.Common.Mvvm;

namespace CDTag.ViewModels.Tools
{
    public interface IEncodingInspectorViewModel : IViewModelBase
    {
        /// <summary>Adds MP3/FLAC/WAV files (folders are searched, subfolders included) and shows the first.</summary>
        void AddFiles(IEnumerable<string> paths);
    }
}
