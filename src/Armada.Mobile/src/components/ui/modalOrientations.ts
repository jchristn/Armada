import type { ModalProps } from 'react-native';

/**
 * Every orientation, for every platform Modal (sheets, dialogs, page sheets). iOS presents a Modal only in the
 * orientations it lists and defaults to portrait, so without this a sheet opened on a phone in landscape rotated
 * the whole app back to portrait. Android ignores the prop.
 */
export const MODAL_ORIENTATIONS: NonNullable<ModalProps['supportedOrientations']> = [
  'portrait',
  'portrait-upside-down',
  'landscape',
  'landscape-left',
  'landscape-right',
];
