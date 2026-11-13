export interface StorageFile { path: string; bytes: number; lastWriteUtc: string }
export interface StorageLocation {
  category: string; path: string; entityId: string | null; activity: string | null;
  bytes: number | null; fileCount: number; oldestWriteUtc: string | null; newestWriteUtc: string | null;
  status: string; warnings: string[]; largestFiles: StorageFile[];
}
export interface StorageHostReport {
  hostId: string; name: string; measuredUtc: string; stale: boolean; status: string; intervalSeconds?: number;
  locations: StorageLocation[]; volumes: { path: string; totalBytes: number | null; availableBytes: number | null; status: string }[]; warnings: string[];
}
export interface StorageDatabase {
  provider: string; measuredUtc: string; allocatedBytes: number | null; unusedBytes: number | null; status: string; warnings: string[]; tables: StorageTable[];
}
export interface StorageTable { schema: string; name: string; rows: number | null; countKind: string; dataBytes: number | null; logicalBytes?: number | null; indexBytes: number | null; warning: string | null }
export interface StorageHistoryPoint {
  measuredUtc: string; observedFileBytes: number; databaseBytes: number | null;
  categories: Record<string, number>; hosts: Record<string, number>; tables: Record<string, number>;
}
export interface StorageSnapshot {
  measuredUtc: string | null; collecting: boolean; lastError: string | null;
  database: StorageDatabase; history: StorageHistoryPoint[];
  /** Host reports are paged with listStorageHosts; these summarize every host. */
  hostCount: number; freshHostCount: number; observedFileBytes: number;
  retention: Record<string, number>; intervalSeconds: number; historyCapacity: number;
}

/** Background collection bounds returned by the administrator settings API. */
export interface StorageSettings { intervalSeconds: number; timeoutSeconds: number; maxEntries: number; largestFileLimit: number; historyCapacity: number; maxLocations: number }
