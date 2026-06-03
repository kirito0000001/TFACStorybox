namespace GalExcleTools.ViewModels;

internal sealed class AssetLibraryViewModel : ObservableObject
{
    private string? _selectedAssetLibraryThumbnailPath;
    private AssetLibraryInfo? _currentAssetLibrary;
    private CharacterInfo? _currentCharacter;
    private bool _isNormalizingBackgroundImages;
    private bool _isNormalizingMusicFiles;
    private bool _isNormalizingAmbientSoundFiles;
    private bool _isNormalizingSoundEffectFiles;
    private bool _isNormalizingCharacterClothes;
    private bool _isNormalizingCharacterFaces;
    private bool _isNormalizingCharacterAdorns;
    private bool _isReorderingCharacterFilters;
    private bool _isRepairingCharacterFilters;
    private bool _isApplyingAssetLibraryMetadata;
    private string? _viewingBackgroundImagePath;
    private CharacterLayerViewerState? _viewingCharacterLayer;
    private bool _isViewingCharacterComposite;
    private string? _playingMusicPath;
    private AudioAssetKind _playingAudioKind = AudioAssetKind.Music;
    private string? _selectedCharacterClothPath;
    private string? _selectedCharacterFacePath;
    private string? _selectedCharacterAdornPath;
    private string? _selectedCharacterVfxPath;

    public string? SelectedAssetLibraryThumbnailPath
    {
        get => _selectedAssetLibraryThumbnailPath;
        set => SetProperty(ref _selectedAssetLibraryThumbnailPath, value);
    }

    public AssetLibraryInfo? CurrentAssetLibrary
    {
        get => _currentAssetLibrary;
        set => SetProperty(ref _currentAssetLibrary, value);
    }

    public CharacterInfo? CurrentCharacter
    {
        get => _currentCharacter;
        set => SetProperty(ref _currentCharacter, value);
    }

    public bool IsNormalizingBackgroundImages
    {
        get => _isNormalizingBackgroundImages;
        set => SetProperty(ref _isNormalizingBackgroundImages, value);
    }

    public bool IsNormalizingMusicFiles
    {
        get => _isNormalizingMusicFiles;
        set => SetProperty(ref _isNormalizingMusicFiles, value);
    }

    public bool IsNormalizingAmbientSoundFiles
    {
        get => _isNormalizingAmbientSoundFiles;
        set => SetProperty(ref _isNormalizingAmbientSoundFiles, value);
    }

    public bool IsNormalizingSoundEffectFiles
    {
        get => _isNormalizingSoundEffectFiles;
        set => SetProperty(ref _isNormalizingSoundEffectFiles, value);
    }

    public bool IsNormalizingCharacterClothes
    {
        get => _isNormalizingCharacterClothes;
        set => SetProperty(ref _isNormalizingCharacterClothes, value);
    }

    public bool IsNormalizingCharacterFaces
    {
        get => _isNormalizingCharacterFaces;
        set => SetProperty(ref _isNormalizingCharacterFaces, value);
    }

    public bool IsNormalizingCharacterAdorns
    {
        get => _isNormalizingCharacterAdorns;
        set => SetProperty(ref _isNormalizingCharacterAdorns, value);
    }

    public bool IsReorderingCharacterFilters
    {
        get => _isReorderingCharacterFilters;
        set => SetProperty(ref _isReorderingCharacterFilters, value);
    }

    public bool IsRepairingCharacterFilters
    {
        get => _isRepairingCharacterFilters;
        set => SetProperty(ref _isRepairingCharacterFilters, value);
    }

    public bool IsApplyingAssetLibraryMetadata
    {
        get => _isApplyingAssetLibraryMetadata;
        set => SetProperty(ref _isApplyingAssetLibraryMetadata, value);
    }

    public string? ViewingBackgroundImagePath
    {
        get => _viewingBackgroundImagePath;
        set => SetProperty(ref _viewingBackgroundImagePath, value);
    }

    public CharacterLayerViewerState? ViewingCharacterLayer
    {
        get => _viewingCharacterLayer;
        set => SetProperty(ref _viewingCharacterLayer, value);
    }

    public bool IsViewingCharacterComposite
    {
        get => _isViewingCharacterComposite;
        set => SetProperty(ref _isViewingCharacterComposite, value);
    }

    public string? PlayingMusicPath
    {
        get => _playingMusicPath;
        set => SetProperty(ref _playingMusicPath, value);
    }

    public AudioAssetKind PlayingAudioKind
    {
        get => _playingAudioKind;
        set => SetProperty(ref _playingAudioKind, value);
    }

    public string? SelectedCharacterClothPath
    {
        get => _selectedCharacterClothPath;
        set => SetProperty(ref _selectedCharacterClothPath, value);
    }

    public string? SelectedCharacterFacePath
    {
        get => _selectedCharacterFacePath;
        set => SetProperty(ref _selectedCharacterFacePath, value);
    }

    public string? SelectedCharacterAdornPath
    {
        get => _selectedCharacterAdornPath;
        set => SetProperty(ref _selectedCharacterAdornPath, value);
    }

    public string? SelectedCharacterVfxPath
    {
        get => _selectedCharacterVfxPath;
        set => SetProperty(ref _selectedCharacterVfxPath, value);
    }
}
