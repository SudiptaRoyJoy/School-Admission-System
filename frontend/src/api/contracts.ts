export type HoldStatus = 'HELD' | 'CONFIRMED' | 'EXPIRED' | 'CANCELLED';

export interface AdmissionClass {
  id: string;
  name: string;
  totalSeats: number;
  heldSeats: number;
  confirmedSeats: number;
  availableSeats: number;
}

export interface CreateClassResponse {
  id: string;
  name: string;
  totalSeats: number;
}

export interface AdmissionHold {
  holdId: string;
  classId: string;
  className: string;
  status: HoldStatus;
  expiresAt: string;
}

export interface CreateHoldResponse {
  holdId: string;
  classId: string;
  studentId: string;
  status: 'HELD';
  expiresAt: string;
}

export interface HoldDetails {
  holdId: string;
  classId: string;
  status: HoldStatus;
  expiresAt: string;
}

export interface ConfirmHoldResponse {
  holdId: string;
  status: 'CONFIRMED';
}

export interface ApiErrorPayload {
  error: {
    code: string;
    message: string;
    details?: Array<{ field: string; message: string }>;
    traceId?: string;
  };
}
