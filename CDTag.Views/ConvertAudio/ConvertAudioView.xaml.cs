using System.Windows.Controls;
using CDTag.Common;
using CDTag.ViewModels.ConvertAudio;

namespace CDTag.Views.ConvertAudio
{
    /// <summary>Convert Audio tab (CD-Tag 2.x layout).</summary>
    public partial class ConvertAudioView : UserControl
    {
        public ConvertAudioView()
        {
            InitializeComponent();
            DataContext = IoC.Resolve<ConvertAudioViewModel>();
        }
    }
}
