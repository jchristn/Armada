namespace Armada.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using Armada.Publisher.Manifest;
    using Armada.Publisher.Preflight;

    /// <summary>
    /// Resolves a channel kind to its implementation. Kinds whose recipes are authored return the concrete
    /// channel; the remainder return a StubChannel that still advertises the tooling it will require so the
    /// Doctor preflight reports the full toolchain even before every recipe is written.
    /// </summary>
    public static class ChannelFactory
    {
        #region Public-Methods

        /// <summary>
        /// Create the channel implementation for a kind.
        /// </summary>
        /// <param name="kind">Channel kind from the manifest.</param>
        /// <returns>An IChannel implementation (concrete or stub).</returns>
        public static IChannel Create(ChannelKindEnum kind)
        {
            switch (kind)
            {
                case ChannelKindEnum.NuGet:
                    return new NuGetChannel();
                case ChannelKindEnum.Inno:
                    return new InnoChannel();
                case ChannelKindEnum.DebRpm:
                    return new DebRpmChannel();

                case ChannelKindEnum.Wix:
                    return new StubChannel(kind, new List<ToolRequirement>
                    {
                        new ToolRequirement("wix", "dotnet tool install --global wix", "win"),
                        new ToolRequirement("signtool", "Install the Windows SDK (ships on windows-latest).", "win")
                    });
                case ChannelKindEnum.Dmg:
                    return new StubChannel(kind, new List<ToolRequirement>
                    {
                        new ToolRequirement("create-dmg", "brew install create-dmg", "osx"),
                        new ToolRequirement("codesign", "Install Xcode command-line tools: xcode-select --install", "osx"),
                        new ToolRequirement("notarytool", "Ships with Xcode command-line tools on macos-latest.", "osx")
                    });
                case ChannelKindEnum.Pkg:
                    return new StubChannel(kind, new List<ToolRequirement>
                    {
                        new ToolRequirement("pkgbuild", "Ships with Xcode command-line tools: xcode-select --install", "osx"),
                        new ToolRequirement("codesign", "Install Xcode command-line tools: xcode-select --install", "osx")
                    });
                case ChannelKindEnum.AppImage:
                    return new StubChannel(kind, new List<ToolRequirement>
                    {
                        new ToolRequirement("appimagetool", "Download appimagetool AppImage from https://github.com/AppImage/AppImageKit/releases and chmod +x it.", "linux")
                    });
                case ChannelKindEnum.Homebrew:
                    return new StubChannel(kind, new List<ToolRequirement>
                    {
                        new ToolRequirement("git", "Install git; the channel commits a formula/cask to the tap repo.")
                    });
                case ChannelKindEnum.Scoop:
                    return new StubChannel(kind, new List<ToolRequirement>
                    {
                        new ToolRequirement("git", "Install git; the channel commits a manifest to the scoop bucket repo.")
                    });
                case ChannelKindEnum.Chocolatey:
                    return new StubChannel(kind, new List<ToolRequirement>
                    {
                        new ToolRequirement("choco", "Install Chocolatey from https://chocolatey.org/install", "win")
                    });
                case ChannelKindEnum.Winget:
                    return new StubChannel(kind, new List<ToolRequirement>
                    {
                        new ToolRequirement("wingetcreate", "winget install wingetcreate", "win")
                    });
                case ChannelKindEnum.Snap:
                    return new StubChannel(kind, new List<ToolRequirement>
                    {
                        new ToolRequirement("snapcraft", "sudo snap install snapcraft --classic", "linux")
                    });
                case ChannelKindEnum.Flatpak:
                    return new StubChannel(kind, new List<ToolRequirement>
                    {
                        new ToolRequirement("flatpak-builder", "sudo apt-get install -y flatpak-builder", "linux")
                    });

                default:
                    throw new NotSupportedException("Unknown channel kind '" + kind + "'.");
            }
        }

        #endregion
    }
}
