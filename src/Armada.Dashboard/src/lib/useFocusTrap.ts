// The focus trap now lives in the shared dialog stack so nested dialogs (a confirm over a drawer) hand the keyboard
// to the top one. Kept as a module so existing imports keep working.
export { useFocusTrap } from './dialogA11y';
