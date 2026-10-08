// Config plugin: keep app content out of the Android Recents (overview) thumbnail.
//
// Android takes the Recents thumbnail as the activity pauses, before JavaScript can draw PrivacyOverlay, so the
// overlay alone leaves the last screen (a conversation, a diff, a log) in Recents. Activity.setRecentsScreenshotEnabled
// (false) (API 33+) makes Recents show a blank card for the app instead, without FLAG_SECURE, so the user's own
// screenshots and screen sharing keep working. Older Android versions keep the overlay's best effort.
// Limit (measured on API 36): opening Overview straight from the app animates the live window into the card, which
// shows content until the card is redrawn; once the app is in the background (Home, another app) the card is blank.
// Only FLAG_SECURE covers that live transition too, at the cost of the user's screenshots (a maintainer decision).
const { withMainActivity } = require('expo/config-plugins');

const MARKER = '// @armada recents-privacy';
const SNIPPET = [
  `    ${MARKER}: Recents shows no app content (API 33+); screenshots and screen sharing still work.`,
  '    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {',
  '      setRecentsScreenshotEnabled(false)',
  '    }',
].join('\n');

/** Pure: MainActivity.kt with the Recents privacy call after super.onCreate (idempotent). */
function addRecentsPrivacy(src) {
  if (src.includes(MARKER)) return src;
  const onCreate = /(\n(\s*)super\.onCreate\([^)]*\)\n)/;
  if (!onCreate.test(src) || !src.includes('import android.os.Build')) {
    throw new Error('withAndroidRecentsPrivacy: the MainActivity template changed; update plugins/withAndroidRecentsPrivacy.js');
  }
  return src.replace(onCreate, `$1${SNIPPET}\n`);
}

function withAndroidRecentsPrivacy(config) {
  return withMainActivity(config, (cfg) => {
    if (cfg.modResults.language !== 'kt') {
      throw new Error('withAndroidRecentsPrivacy: expected a Kotlin MainActivity');
    }
    cfg.modResults.contents = addRecentsPrivacy(cfg.modResults.contents);
    return cfg;
  });
}

module.exports = withAndroidRecentsPrivacy;
module.exports.addRecentsPrivacy = addRecentsPrivacy;
