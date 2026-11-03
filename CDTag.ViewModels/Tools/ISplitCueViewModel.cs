using CDTag.Common.Mvvm;

namespace CDTag.ViewModels.Tools
{
    public interface ISplitCueViewModel : IViewModelBase
    {
        /// <summary>Lists the .cue files in <paramref name="folder"/> and shows <paramref name="cueFile"/>, or the first one.</summary>
        void Load(string folder, string cueFile = null);

        /// <summary>True after a split finished (the file list should be refreshed).</summary>
        bool HasSplit { get; }
    }
}
