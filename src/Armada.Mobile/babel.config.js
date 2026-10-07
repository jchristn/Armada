// Project-wide Babel config (also applied to the dashboard modules shared through @dashboard/*).
module.exports = function babelConfig(api) {
  api.cache(true);
  return { presets: ['babel-preset-expo'] };
};
