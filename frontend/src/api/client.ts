import type {
  AdmissionClass,
  AdmissionHold,
  ApiErrorPayload,
  ConfirmHoldResponse,
  CreateClassResponse,
  CreateHoldResponse,
} from './contracts';

const configuredBaseUrl = window.__APP_CONFIG__?.API_BASE_URL
  ?? import.meta.env.VITE_API_BASE_URL
  ?? 'http://localhost:8080';

export const API_BASE_URL = configuredBaseUrl.replace(/\/+$/, '');
let serverClockOffsetMs = 0;

export function estimatedServerNow(): number {
  return Date.now() + serverClockOffsetMs;
}

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code: string,
    public readonly details: Array<{ field: string; message: string }> = [],
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  let response: Response;
  try {
    const headers = new Headers(init.headers);
    headers.set('Accept', 'application/json');
    if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
    response = await fetch(`${API_BASE_URL}${path}`, {
      ...init,
      headers,
      cache: 'no-store',
    });
  } catch {
    throw new ApiError('Unable to reach the admission service. Check your connection and try again.', 0, 'NETWORK_ERROR');
  }

  const serverUtc = response.headers.get('X-Server-Utc');
  if (serverUtc) {
    const parsed = Date.parse(serverUtc);
    if (Number.isFinite(parsed)) serverClockOffsetMs = parsed - Date.now();
  }

  if (!response.ok) {
    let payload: ApiErrorPayload | undefined;
    try {
      payload = await response.json() as ApiErrorPayload;
    } catch {
      // A reverse proxy or malformed response can omit the standard envelope.
    }
    throw new ApiError(
      payload?.error?.message ?? 'The request could not be completed. Please try again.',
      response.status,
      payload?.error?.code ?? 'UNKNOWN_ERROR',
      payload?.error?.details ?? [],
    );
  }

  if (response.status === 204) return undefined as T;
  return await response.json() as T;
}

function studentHeaders(studentId: string): HeadersInit {
  return { 'X-Student-Id': studentId };
}

export const api = {
  health: () => request<{ status: string }>('/health'),
  getClasses: () => request<AdmissionClass[]>('/classes'),
  getMyHolds: (studentId: string) => request<AdmissionHold[]>('/me/holds', {
    headers: studentHeaders(studentId),
  }),
  createClass: (name: string, totalSeats: number) => request<CreateClassResponse>('/classes', {
    method: 'POST',
    body: JSON.stringify({ name, totalSeats }),
  }),
  apply: (classId: string, studentId: string) => request<CreateHoldResponse>(`/classes/${encodeURIComponent(classId)}/holds`, {
    method: 'POST',
    headers: studentHeaders(studentId),
  }),
  confirm: (holdId: string, studentId: string) => request<ConfirmHoldResponse>(`/holds/${encodeURIComponent(holdId)}/confirm`, {
    method: 'POST',
    headers: studentHeaders(studentId),
  }),
  cancel: (holdId: string, studentId: string) => request<void>(`/holds/${encodeURIComponent(holdId)}`, {
    method: 'DELETE',
    headers: studentHeaders(studentId),
  }),
};
