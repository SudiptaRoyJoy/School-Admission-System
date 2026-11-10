import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import App from './App';
import type { AdmissionClass, AdmissionHold } from '../api/contracts';

const initialClass: AdmissionClass = {
  id: 'c_0123456789abcdef0123456789abcdef',
  name: 'Class 6 - Morning Shift',
  totalSeats: 120,
  heldSeats: 12,
  confirmedSeats: 70,
  availableSeats: 38,
};

describe('Admission portal', () => {
  let classes: AdmissionClass[];
  let holds: AdmissionHold[];
  let nextClassNumber: number;
  let nextHoldNumber: number;

  beforeEach(() => {
    localStorage.clear();
    classes = [{ ...initialClass }];
    holds = [];
    nextClassNumber = 1;
    nextHoldNumber = 1;

    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';
      const headers = new Headers({ 'X-Server-Utc': new Date().toISOString() });
      const json = (payload: unknown, status = 200) => ({
        ok: status >= 200 && status < 300,
        status,
        headers,
        json: async () => payload,
      } as Response);

      if (url.endsWith('/health')) return json({ status: 'ok' });
      if (url.endsWith('/classes') && method === 'GET') return json(classes);
      if (url.endsWith('/me/holds')) return json(holds);

      if (url.endsWith('/classes') && method === 'POST') {
        const body = JSON.parse(String(init?.body ?? '{}')) as { name: string; totalSeats: number };
        const created: AdmissionClass = {
          id: `c_test${String(nextClassNumber++).padStart(28, '0')}`,
          name: body.name,
          totalSeats: body.totalSeats,
          heldSeats: 0,
          confirmedSeats: 0,
          availableSeats: body.totalSeats,
        };
        classes = [...classes, created];
        return json({ id: created.id, name: created.name, totalSeats: created.totalSeats }, 201);
      }

      const applyMatch = url.match(/\/classes\/([^/]+)\/holds$/);
      if (applyMatch && method === 'POST') {
        const classId = decodeURIComponent(applyMatch[1]);
        const studentId = new Headers(init?.headers).get('X-Student-Id') ?? '';
        const target = classes.find(item => item.id === classId);
        if (!target || target.availableSeats <= 0) {
          return json({ error: { code: 'NO_SEATS_AVAILABLE', message: 'No seats available.' } }, 409);
        }
        const holdId = `h_test${String(nextHoldNumber++).padStart(28, '0')}`;
        const hold: AdmissionHold = {
          holdId,
          classId,
          className: target.name,
          status: 'HELD',
          expiresAt: new Date(Date.now() + 120_000).toISOString(),
        };
        holds = [hold, ...holds];
        classes = classes.map(item => item.id === classId
          ? { ...item, heldSeats: item.heldSeats + 1, availableSeats: item.availableSeats - 1 }
          : item);
        return json({ holdId, classId, studentId, status: 'HELD', expiresAt: hold.expiresAt }, 201);
      }

      const confirmMatch = url.match(/\/holds\/([^/]+)\/confirm$/);
      if (confirmMatch && method === 'POST') {
        const holdId = decodeURIComponent(confirmMatch[1]);
        holds = holds.map(item => item.holdId === holdId ? { ...item, status: 'CONFIRMED' } : item);
        const found = holds.find(item => item.holdId === holdId);
        if (!found) return json({ error: { code: 'HOLD_NOT_FOUND', message: 'Not found.' } }, 404);
        classes = classes.map(item => item.id === found.classId
          ? { ...item, heldSeats: Math.max(0, item.heldSeats - 1), confirmedSeats: item.confirmedSeats + 1 }
          : item);
        return json({ holdId, status: 'CONFIRMED' });
      }

      const cancelMatch = url.match(/\/holds\/([^/]+)$/);
      if (cancelMatch && method === 'DELETE') {
        const holdId = decodeURIComponent(cancelMatch[1]);
        const found = holds.find(item => item.holdId === holdId);
        if (found) {
          holds = holds.map(item => item.holdId === holdId ? { ...item, status: 'CANCELLED' } : item);
          classes = classes.map(item => item.id === found.classId
            ? { ...item, heldSeats: Math.max(0, item.heldSeats - 1), availableSeats: item.availableSeats + 1 }
            : item);
        }
        return { ok: true, status: 204, headers, json: async () => undefined } as unknown as Response;
      }

      return json({ error: { code: 'ROUTE_NOT_FOUND', message: 'Not found.' } }, 404);
    }));
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('loads class and shift seat counts from the API', async () => {
    render(<App />);
    expect(await screen.findByRole('heading', { name: 'Class 6 - Morning Shift' })).toBeInTheDocument();
    expect(screen.getByLabelText('82 of 120 seats occupied or on hold')).toBeInTheDocument();
    expect(screen.getByText(/My applications/)).toBeInTheDocument();
  });

  it('lets a parent apply and confirm a held seat from the UI', async () => {
    const user = userEvent.setup();
    render(<App />);

    await screen.findByRole('heading', { name: 'Class 6 - Morning Shift' });
    await user.type(screen.getByLabelText('Student birth-registration number'), '20152691234567890');
    await user.click(screen.getByRole('button', { name: /Use this ID/ }));
    await user.click(screen.getByRole('button', { name: /Apply for a seat/ }));

    expect(await screen.findByText('Seat on hold')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Simulate fee payment/ }));
    expect(await screen.findByText('Your seat is confirmed. Welcome aboard!')).toBeInTheDocument();
    await waitFor(() => expect(holds[0]?.status).toBe('CONFIRMED'));
  });

  it('lets the school admin create a class and shift pool', async () => {
    const user = userEvent.setup();
    render(<App />);

    await screen.findByRole('heading', { name: 'Class 6 - Morning Shift' });
    await user.click(screen.getByRole('button', { name: /School admin/ }));
    await user.type(screen.getByLabelText('Class and shift name'), 'Class 7 - Day Shift');
    await user.clear(screen.getByLabelText('Total seats'));
    await user.type(screen.getByLabelText('Total seats'), '45');
    await user.click(screen.getByRole('button', { name: /Create admission group/ }));

    expect(await screen.findByRole('heading', { name: 'Class 7 - Day Shift' })).toBeInTheDocument();
    await waitFor(() => expect(classes.some(item => item.name === 'Class 7 - Day Shift' && item.totalSeats === 45)).toBe(true));
  });
});
