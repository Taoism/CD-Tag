using System;
using System.ComponentModel;
using System.Diagnostics;
using CDTag.Common;
using CDTag.Common.Events;
using CDTag.Common.Model;

namespace CDTag.Model.Profile
{
    public class FileNaming : ModelBase<FileNaming>
    {
        private bool _savedUseLatinCharactersOnly;

        public FileNaming()
        {
            SingleCD = new NamingFormatGroup();
            MultiCD = new NamingFormatGroup();
            Vinyl = new NamingFormatGroup();

            EnhancedPropertyChanged += FileNaming_EnhancedPropertyChanged;
        }

        private void FileNaming_EnhancedPropertyChanged(object sender, EnhancedPropertyChangedEventArgs<FileNaming> e)
        {
            if (e.IsProperty(p => p.UseStandardCharactersOnly))
            {
                if (UseStandardCharactersOnly)
                {
                    _savedUseLatinCharactersOnly = UseLatinCharactersOnly;
                    UseLatinCharactersOnly = true;
                }
                else
                {
                    UseLatinCharactersOnly = _savedUseLatinCharactersOnly;
                }
            }
        }

        public NamingFormatGroup SingleCD
        {
            get { return Get<NamingFormatGroup>(nameof(SingleCD)); }
            set { Set(nameof(SingleCD), value); }
        }

        public NamingFormatGroup MultiCD
        {
            get { return Get<NamingFormatGroup>(nameof(MultiCD)); }
            set { Set(nameof(MultiCD), value); }
        }

        public NamingFormatGroup Vinyl
        {
            get { return Get<NamingFormatGroup>(nameof(Vinyl)); }
            set { Set(nameof(Vinyl), value); }
        }

        public int? FileCharacterLimit
        {
            get { return Get<int?>(nameof(FileCharacterLimit)); }
            set { Set(nameof(FileCharacterLimit), value); }
        }

        public int? DirectoryCharacterLimit
        {
            get { return Get<int?>(nameof(DirectoryCharacterLimit)); }
            set { Set(nameof(DirectoryCharacterLimit), value); }
        }

        public bool UseUnderscores
        {
            get { return Get<bool>(nameof(UseUnderscores)); }
            set { Set(nameof(UseUnderscores), value); }
        }

        public bool UseStandardCharactersOnly
        {
            get { return Get<bool>(nameof(UseStandardCharactersOnly)); }
            set { Set(nameof(UseStandardCharactersOnly), value); }
        }

        public bool UseLatinCharactersOnly
        {
            get { return Get<bool>(nameof(UseLatinCharactersOnly)); }
            set { Set(nameof(UseLatinCharactersOnly), value); }
        }

        public bool DontChangeCaseWordsWithAllCaps
        {
            get { return Get<bool>(nameof(DontChangeCaseWordsWithAllCaps)); }
            set { Set(nameof(DontChangeCaseWordsWithAllCaps), value); }
        }

        public bool DontChangeCaseWordsWithNonLetter
        {
            get { return Get<bool>(nameof(DontChangeCaseWordsWithNonLetter)); }
            set { Set(nameof(DontChangeCaseWordsWithNonLetter), value); }
        }
    }
}
