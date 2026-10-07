// Config plugin: adopt the UIScene life cycle on iOS.
//
// Apps built with the iOS 27 SDK (Xcode 27) are terminated at launch unless they use scene-based life cycle.
// Expo SDK 57 ships the scene delegate (ExpoAppSceneDelegate, Objective-C name EXExpoAppSceneDelegate) but its
// prebuild template still starts React Native from the app delegate. This plugin applies the SDK 58 template's
// change: register the scene delegate in Info.plist and let it, not the app delegate, create the window.
// Remove the plugin once the app moves to an SDK whose template does this itself.
const { withAppDelegate, withInfoPlist } = require('expo/config-plugins');

const SCENE_DELEGATE = 'EXExpoAppSceneDelegate';

function withSceneManifest(config) {
  return withInfoPlist(config, (cfg) => {
    cfg.modResults.UIApplicationSceneManifest = {
      UIApplicationSupportsMultipleScenes: false,
      UISceneConfigurations: {
        UIWindowSceneSessionRoleApplication: [
          { UISceneConfigurationName: 'Default Configuration', UISceneDelegateClassName: SCENE_DELEGATE },
        ],
      },
    };
    return cfg;
  });
}

function withSceneAppDelegate(config) {
  return withAppDelegate(config, (cfg) => {
    if (cfg.modResults.language !== 'swift') {
      throw new Error('withSceneLifecycle: expected a Swift AppDelegate');
    }
    let src = cfg.modResults.contents;
    src = src.replace(
      /class AppDelegate: ExpoAppDelegate \{/,
      'class AppDelegate: ExpoAppDelegate, ExpoReactNativeFactoryProvider {',
    );
    src = src.replace(
      /#if os\(iOS\) \|\| os\(tvOS\)\n\s*window = UIWindow\(frame: UIScreen\.main\.bounds\)\n\s*factory\.startReactNative\([\s\S]*?\)\n#endif\n/,
      '    // The scene delegate creates the window and starts React Native (scene life cycle, iOS 27 SDK).\n',
    );
    if (!src.includes('ExpoReactNativeFactoryProvider') || src.includes('factory.startReactNative(')) {
      throw new Error('withSceneLifecycle: the AppDelegate template changed; update plugins/withSceneLifecycle.js');
    }
    cfg.modResults.contents = src;
    return cfg;
  });
}

module.exports = function withSceneLifecycle(config) {
  return withSceneAppDelegate(withSceneManifest(config));
};
