/*
 * A minimal fake of the `vscode` module, so unit tests run under the plain Node test runner without launching VS Code.
 * test/setup.ts redirects `require('vscode')` here. Tests script user responses through `fake` and inspect what the
 * extension did through its records.
 */
/* eslint-disable @typescript-eslint/no-explicit-any */
import * as path from 'path';

type Responder = ((...args: any[]) => any) | any;

/** Scripted responses and call records. */
export const fake = {
  config: new Map<string, unknown>(),
  configUpdates: [] as Array<{ key: string; value: unknown }>,
  messages: [] as Array<{ level: 'info' | 'warning' | 'error'; text: string; options?: unknown; items: string[] }>,
  executed: [] as Array<{ command: string; args: unknown[] }>,
  registered: new Map<string, (...args: any[]) => any>(),
  availableCommands: [] as string[],
  clipboard: '',
  openedExternal: [] as string[],
  openExternalResult: true,
  documents: [] as Array<{ language?: string; content?: string; uri: Uri }>,
  writtenFiles: new Map<string, string>(),
  workspaceFolders: [] as Array<{ uri: Uri; name: string; index: number }>,
  quickPick: [] as Responder[],
  inputBox: [] as Responder[],
  warning: [] as Responder[],
  info: [] as Responder[],
  error: [] as Responder[],
  executeHandlers: new Map<string, (...args: any[]) => any>(),
  reset(): void {
    this.config.clear();
    this.configUpdates = [];
    this.messages = [];
    this.executed = [];
    this.registered.clear();
    this.availableCommands = [];
    this.clipboard = '';
    this.openedExternal = [];
    this.openExternalResult = true;
    this.documents = [];
    this.writtenFiles.clear();
    this.workspaceFolders = [];
    this.quickPick = [];
    this.inputBox = [];
    this.warning = [];
    this.info = [];
    this.error = [];
    this.executeHandlers.clear();
  },
};

function respond(queue: Responder[], ...args: any[]): any {
  if (!queue.length) return undefined;
  const next = queue.shift();
  return typeof next === 'function' ? next(...args) : next;
}

export class Disposable {
  private readonly _Fn: () => void;
  public constructor(fn: () => void) {
    this._Fn = fn;
  }
  public dispose(): void {
    this._Fn();
  }
}

export class EventEmitter<T> {
  private readonly _Listeners = new Set<(e: T) => void>();
  public readonly event = (listener: (e: T) => void): Disposable => {
    this._Listeners.add(listener);
    return new Disposable(() => this._Listeners.delete(listener));
  };
  public fireCount = 0;
  public fire(e: T): void {
    this.fireCount++;
    for (const l of Array.from(this._Listeners)) l(e);
  }
  public dispose(): void {
    this._Listeners.clear();
  }
}

export class Uri {
  public readonly scheme: string;
  public readonly fsPath: string;
  public readonly path: string;
  private constructor(scheme: string, p: string) {
    this.scheme = scheme;
    this.path = p;
    this.fsPath = p;
  }
  public static file(p: string): Uri {
    return new Uri('file', path.resolve(p));
  }
  public static parse(value: string): Uri {
    const match = /^([a-zA-Z][\w+.-]*):(.*)$/.exec(value);
    const uri = new Uri(match ? match[1] : 'file', match ? match[2] : value);
    (uri as any)._Raw = value;
    return uri;
  }
  public static joinPath(base: Uri, ...parts: string[]): Uri {
    return new Uri(base.scheme, path.join(base.path, ...parts));
  }
  public toString(): string {
    return (this as any)._Raw ?? `${this.scheme}://${this.path}`;
  }
}

export enum TreeItemCollapsibleState { None = 0, Collapsed = 1, Expanded = 2 }
export enum ViewColumn { Active = -1, Beside = -2, One = 1 }
export enum ConfigurationTarget { Global = 1, Workspace = 2, WorkspaceFolder = 3 }

export class ThemeIcon {
  public constructor(public readonly id: string, public readonly color?: ThemeColor) {}
}

export class ThemeColor {
  public constructor(public readonly id: string) {}
}

export class MarkdownString {
  public value: string;
  public constructor(value?: string, public readonly supportThemeIcons?: boolean) {
    this.value = value ?? '';
  }
  public appendMarkdown(text: string): MarkdownString {
    this.value += text;
    return this;
  }
}

export class TreeItem {
  public id?: string;
  public description?: string;
  public tooltip?: string | MarkdownString;
  public iconPath?: ThemeIcon;
  public contextValue?: string;
  public command?: { command: string; title: string; arguments?: unknown[] };
  public constructor(public label: string, public collapsibleState?: TreeItemCollapsibleState) {}
}

export class FileDecoration {
  public propagate?: boolean;
  public constructor(public badge?: string, public tooltip?: string, public color?: ThemeColor) {}
}

function message(level: 'info' | 'warning' | 'error', queue: Responder[], text: string, ...rest: any[]): Promise<any> {
  let options: unknown;
  let items = rest;
  if (rest.length && rest[0] && typeof rest[0] === 'object' && !Array.isArray(rest[0])) {
    options = rest[0];
    items = rest.slice(1);
  }
  fake.messages.push({ level, text, options, items });
  return Promise.resolve(respond(queue, text, options, items));
}

export const window = {
  showInformationMessage: (text: string, ...rest: any[]) => message('info', fake.info, text, ...rest),
  showWarningMessage: (text: string, ...rest: any[]) => message('warning', fake.warning, text, ...rest),
  showErrorMessage: (text: string, ...rest: any[]) => message('error', fake.error, text, ...rest),
  showQuickPick: (items: any, options?: any) => Promise.resolve(items).then((resolved) => respond(fake.quickPick, resolved, options)),
  showInputBox: (options?: any) => Promise.resolve(respond(fake.inputBox, options)),
  showTextDocument: (doc: any) => Promise.resolve({ document: doc }),
  createOutputChannel: () => ({ appendLine: () => undefined, dispose: () => undefined }),
  createTreeView: () => ({ badge: undefined, description: undefined, dispose: () => undefined }),
  registerFileDecorationProvider: () => new Disposable(() => undefined),
  createWebviewPanel: () => {
    throw new Error('webviews are not faked');
  },
};

export const commands = {
  registerCommand: (id: string, fn: (...args: any[]) => any) => {
    fake.registered.set(id, fn);
    return new Disposable(() => fake.registered.delete(id));
  },
  executeCommand: async (command: string, ...args: unknown[]) => {
    fake.executed.push({ command, args });
    const handler = fake.executeHandlers.get(command);
    return handler ? handler(...args) : undefined;
  },
  getCommands: async () => fake.availableCommands.slice(),
};

export const env = {
  clipboard: {
    writeText: async (text: string) => {
      fake.clipboard = text;
    },
    readText: async () => fake.clipboard,
  },
  openExternal: async (uri: Uri) => {
    fake.openedExternal.push(uri.toString());
    return fake.openExternalResult;
  },
};

export const workspace = {
  get workspaceFolders() {
    return fake.workspaceFolders;
  },
  getConfiguration: (section?: string) => ({
    get: <T>(key: string, defaultValue?: T): T => {
      const full = section ? `${section}.${key}` : key;
      return (fake.config.has(full) ? fake.config.get(full) : defaultValue) as T;
    },
    update: async (key: string, value: unknown) => {
      const full = section ? `${section}.${key}` : key;
      fake.config.set(full, value);
      fake.configUpdates.push({ key: full, value });
    },
  }),
  openTextDocument: async (options: { language?: string; content?: string }) => {
    const doc = { language: options.language, content: options.content, uri: Uri.parse(`untitled:Untitled-${fake.documents.length + 1}`) };
    fake.documents.push(doc);
    return doc;
  },
  fs: {
    createDirectory: async () => undefined,
    writeFile: async (uri: Uri, data: Uint8Array) => {
      fake.writtenFiles.set(uri.fsPath, Buffer.from(data).toString('utf8'));
    },
  },
  onDidChangeWorkspaceFolders: () => new Disposable(() => undefined),
  onDidChangeConfiguration: () => new Disposable(() => undefined),
};

/** In-memory SecretStorage. */
export class FakeSecretStorage {
  public readonly values = new Map<string, string>();
  private readonly _Emitter = new EventEmitter<{ key: string }>();
  public readonly onDidChange = this._Emitter.event;
  public async get(key: string): Promise<string | undefined> {
    return this.values.get(key);
  }
  public async store(key: string, value: string): Promise<void> {
    this.values.set(key, value);
  }
  public async delete(key: string): Promise<void> {
    this.values.delete(key);
  }
  public async keys(): Promise<string[]> {
    return Array.from(this.values.keys());
  }
}
