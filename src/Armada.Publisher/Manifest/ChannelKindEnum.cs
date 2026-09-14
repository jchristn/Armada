namespace Armada.Publisher.Manifest
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The distribution channel a packing step produces. Each value maps to one implementation of IChannel.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChannelKindEnum
    {
        /// <summary>
        /// NuGet package published as a global dotnet tool (dotnet tool install -g).
        /// </summary>
        NuGet,

        /// <summary>
        /// Windows Inno Setup installer (.exe).
        /// </summary>
        Inno,

        /// <summary>
        /// Windows WiX installer (.msi).
        /// </summary>
        Wix,

        /// <summary>
        /// macOS disk image (.dmg) wrapping a signed, notarized .app bundle.
        /// </summary>
        Dmg,

        /// <summary>
        /// macOS installer package (.pkg) for daemons requiring an install script or LaunchDaemon.
        /// </summary>
        Pkg,

        /// <summary>
        /// Linux .deb and .rpm produced from one directory via fpm.
        /// </summary>
        DebRpm,

        /// <summary>
        /// Linux distro-agnostic AppImage.
        /// </summary>
        AppImage,

        /// <summary>
        /// Homebrew formula or cask published to a tap.
        /// </summary>
        Homebrew,

        /// <summary>
        /// Scoop manifest published to a bucket.
        /// </summary>
        Scoop,

        /// <summary>
        /// Chocolatey package (.nuspec + chocolateyInstall.ps1).
        /// </summary>
        Chocolatey,

        /// <summary>
        /// winget manifest submitted to microsoft/winget-pkgs.
        /// </summary>
        Winget,

        /// <summary>
        /// Snap package published to the Snap Store.
        /// </summary>
        Snap,

        /// <summary>
        /// Flatpak package submitted to Flathub.
        /// </summary>
        Flatpak
    }
}
