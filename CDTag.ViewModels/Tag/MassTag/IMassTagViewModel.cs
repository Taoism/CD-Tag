using System.Collections;
using System.Collections.Generic;
using CDTag.Common.Mvvm;

namespace CDTag.ViewModels.Tag.MassTag
{
    public interface IMassTagViewModel : IViewModelBase
    {
        /// <summary>Adds MP3/FLAC files (folders are searched, subfolders included).</summary>
        void AddFiles(IEnumerable<string> paths);

        /// <summary>The rows selected in the list (set by the view); "Set" and "Number" work on these, or on all rows when none are selected.</summary>
        IList SelectedRows { get; set; }

        /// <summary>True after tags were saved (the file list should be refreshed).</summary>
        bool HasSaved { get; }
    }
}
