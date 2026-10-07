import type { SocketLike } from '@dashboard/lib/armadaSocket';

/** A controllable WebSocket for socket lifecycle tests. */
export class FakeSocket implements SocketLike {
  readyState = 0;
  sent: string[] = [];
  closed = false;
  onopen: ((ev: unknown) => void) | null = null;
  onmessage: ((ev: { data: unknown }) => void) | null = null;
  onclose: ((ev: unknown) => void) | null = null;
  onerror: ((ev: unknown) => void) | null = null;

  constructor(public url: string) {}

  send(data: string) { this.sent.push(data); }
  close() { this.closed = true; this.readyState = 3; }
  open() { this.readyState = 1; this.onopen?.({}); }
  drop() { this.readyState = 3; this.onclose?.({}); }
  message(data: unknown) { this.onmessage?.({ data: JSON.stringify(data) }); }
}

export function socketFactory() {
  const sockets: FakeSocket[] = [];
  const factory = (url: string) => {
    const socket = new FakeSocket(url);
    sockets.push(socket);
    return socket;
  };
  return { sockets, factory };
}
