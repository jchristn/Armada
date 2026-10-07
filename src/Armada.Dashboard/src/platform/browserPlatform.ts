import type { ClientPlatformAdapter, DownloadedFile } from '../api/client';

/**
 * Browser implementation of the API client's platform adapter: a downloaded file is saved through a temporary
 * object URL and a synthetic anchor click. Registered from main.tsx; never imported by shared modules.
 */
export const browserPlatform: ClientPlatformAdapter = {
  saveFile: async (file: DownloadedFile) => {
    const url = URL.createObjectURL(file.body);
    const a = document.createElement('a');
    a.href = url;
    a.download = file.filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  },
};
