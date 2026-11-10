import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { FormEvent, ReactNode } from 'react';
import {
  Activity,
  ArrowRight,
  BadgeCheck,
  BookOpen,
  Check,
  CheckCircle2,
  ChevronRight,
  CircleAlert,
  Clock3,
  CreditCard,
  GraduationCap,
  LayoutDashboard,
  LoaderCircle,
  Plus,
  RefreshCw,
  School,
  Settings2,
  ShieldCheck,
  Sparkles,
  Users,
  Wifi,
  WifiOff,
  X,
} from 'lucide-react';
import { api, ApiError, estimatedServerNow } from '../api/client';
import type { AdmissionClass, AdmissionHold, HoldStatus } from '../api/contracts';

const STUDENT_STORAGE_KEY = 'northstar-admission-student-id';
const STUDENT_ID_PATTERN = /^[0-9]{17}$/;

type View = 'parent' | 'admin';
type Toast = { type: 'success' | 'error' | 'info'; title: string; message: string };

function readStoredStudentId(): string {
  try {
    return localStorage.getItem(STUDENT_STORAGE_KEY) ?? '';
  } catch {
    return '';
  }
}

function formatNumber(value: number): string {
  return new Intl.NumberFormat('en-US').format(value);
}

function statusLabel(status: HoldStatus): string {
  switch (status) {
    case 'HELD': return 'Seat on hold';
    case 'CONFIRMED': return 'Admission confirmed';
    case 'EXPIRED': return 'Hold expired';
    case 'CANCELLED': return 'Application cancelled';
  }
}

function statusClass(status: HoldStatus): string {
  return `status-pill status-${status.toLowerCase()}`;
}

function friendlyError(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'NO_SEATS_AVAILABLE': return 'All seats in this class are currently taken or on hold.';
      case 'ALREADY_APPLIED': return 'This student already has an active application for this class.';
      case 'ALREADY_ADMITTED': return 'This student already has a confirmed admission for this class.';
      case 'HOLD_EXPIRED': return 'The seat hold has expired. You can apply again if seats remain.';
      case 'HOLD_ALREADY_CONFIRMED': return 'A confirmed admission cannot be cancelled.';
      case 'NOT_HOLD_OWNER': return 'This application belongs to a different student ID.';
      case 'INVALID_STUDENT_ID': return 'Enter exactly 17 English digits for the student birth-registration number.';
      case 'MISSING_STUDENT_ID': return 'Enter the student birth-registration number first.';
      default: return error.message;
    }
  }
  return 'Something unexpected happened. Please try again.';
}

function remainingMilliseconds(expiresAt: string): number {
  return Math.max(0, Date.parse(expiresAt) - estimatedServerNow());
}

function useCountdown(expiresAt: string): number {
  const [remaining, setRemaining] = useState(() => remainingMilliseconds(expiresAt));
  useEffect(() => {
    const update = () => setRemaining(remainingMilliseconds(expiresAt));
    update();
    const timer = window.setInterval(update, 1000);
    return () => window.clearInterval(timer);
  }, [expiresAt]);
  return remaining;
}

function formatCountdown(milliseconds: number): string {
  const totalSeconds = Math.ceil(milliseconds / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  const hours = Math.floor(minutes / 60);
  const remainingMinutes = minutes % 60;
  if (hours > 0) return `${hours.toString().padStart(2, '0')}:${remainingMinutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
  return `${remainingMinutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
}

function Countdown({ expiresAt }: { expiresAt: string }) {
  const remaining = useCountdown(expiresAt);
  return (
    <span className={`countdown ${remaining <= 60_000 ? 'countdown-urgent' : ''}`} aria-live="off">
      <Clock3 size={14} />
      {remaining > 0 ? `${formatCountdown(remaining)} remaining` : 'Time expired'}
    </span>
  );
}

function SeatBar({ admissionClass }: { admissionClass: AdmissionClass }) {
  const occupied = admissionClass.heldSeats + admissionClass.confirmedSeats;
  const heldPercent = admissionClass.totalSeats > 0 ? (admissionClass.heldSeats / admissionClass.totalSeats) * 100 : 0;
  const confirmedPercent = admissionClass.totalSeats > 0 ? (admissionClass.confirmedSeats / admissionClass.totalSeats) * 100 : 0;
  const usedPercent = Math.min(100, heldPercent + confirmedPercent);
  return (
    <div className="seat-bar-wrap">
      <div className="seat-bar" role="img" aria-label={`${occupied} of ${admissionClass.totalSeats} seats occupied or on hold`}>
        <span className="seat-bar-confirmed" style={{ width: `${confirmedPercent}%` }} />
        <span className="seat-bar-held" style={{ width: `${heldPercent}%` }} />
      </div>
      <div className="seat-bar-caption">
        <span>{formatNumber(occupied)} of {formatNumber(admissionClass.totalSeats)} allocated</span>
        <span>{Math.round(usedPercent)}% filled</span>
      </div>
    </div>
  );
}

function ToastView({ toast, onClose }: { toast: Toast; onClose: () => void }) {
  const Icon = toast.type === 'success' ? CheckCircle2 : toast.type === 'error' ? CircleAlert : Sparkles;
  return (
    <div className={`toast toast-${toast.type}`} role="status">
      <span className="toast-icon"><Icon size={18} /></span>
      <span className="toast-copy"><strong>{toast.title}</strong><small>{toast.message}</small></span>
      <button className="icon-button toast-close" onClick={onClose} aria-label="Dismiss message"><X size={16} /></button>
    </div>
  );
}

function App() {
  const [view, setView] = useState<View>('parent');
  const [studentInput, setStudentInput] = useState(readStoredStudentId);
  const [activeStudentId, setActiveStudentId] = useState(readStoredStudentId);
  const [classes, setClasses] = useState<AdmissionClass[]>([]);
  const [holds, setHolds] = useState<AdmissionHold[]>([]);
  const [holdsLoading, setHoldsLoading] = useState(false);
  const [classesLoading, setClassesLoading] = useState(true);
  const [classesError, setClassesError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [apiOnline, setApiOnline] = useState(true);
  const [toast, setToast] = useState<Toast | null>(null);
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [className, setClassName] = useState('');
  const [totalSeats, setTotalSeats] = useState('120');
  const [creatingClass, setCreatingClass] = useState(false);
  const toastTimer = useRef<number | undefined>(undefined);

  const showToast = useCallback((nextToast: Toast) => {
    setToast(nextToast);
    if (toastTimer.current) window.clearTimeout(toastTimer.current);
    toastTimer.current = window.setTimeout(() => setToast(null), 5200);
  }, []);

  const refreshClasses = useCallback(async (quiet = false) => {
    if (!quiet) setRefreshing(true);
    try {
      const result = await api.getClasses();
      setClasses(result);
      setClassesError(null);
      setApiOnline(true);
    } catch (error) {
      setClassesError(friendlyError(error));
      setApiOnline(false);
      if (!quiet) showToast({ type: 'error', title: 'Could not load classes', message: friendlyError(error) });
    } finally {
      setClassesLoading(false);
      if (!quiet) setRefreshing(false);
    }
  }, [showToast]);

  const refreshHolds = useCallback(async (studentId = activeStudentId, quiet = false) => {
    if (!studentId || !STUDENT_ID_PATTERN.test(studentId)) {
      setHolds([]);
      setHoldsLoading(false);
      return [];
    }
    if (!quiet) setHoldsLoading(true);
    try {
      const result = await api.getMyHolds(studentId);
      setHolds(result);
      setApiOnline(true);
      return result;
    } catch (error) {
      setApiOnline(false);
      if (!quiet) showToast({ type: 'error', title: 'Could not load applications', message: friendlyError(error) });
      return [];
    } finally {
      if (!quiet) setHoldsLoading(false);
    }
  }, [activeStudentId, showToast]);

  const refreshAll = useCallback(async (quiet = false) => {
    await Promise.all([refreshClasses(quiet), refreshHolds(activeStudentId, quiet)]);
  }, [activeStudentId, refreshClasses, refreshHolds]);

  useEffect(() => {
    void refreshClasses();
    const timer = window.setInterval(() => void refreshClasses(true), 5000);
    return () => window.clearInterval(timer);
  }, [refreshClasses]);

  useEffect(() => {
    if (!activeStudentId) {
      setHolds([]);
      setHoldsLoading(false);
      return;
    }
    void refreshHolds(activeStudentId);
    const timer = window.setInterval(() => void refreshHolds(activeStudentId, true), 5000);
    return () => window.clearInterval(timer);
  }, [activeStudentId, refreshHolds]);

  useEffect(() => () => {
    if (toastTimer.current) window.clearTimeout(toastTimer.current);
  }, []);

  const summary = useMemo(() => classes.reduce((result, item) => ({
    total: result.total + item.totalSeats,
    held: result.held + item.heldSeats,
    confirmed: result.confirmed + item.confirmedSeats,
    available: result.available + item.availableSeats,
  }), { total: 0, held: 0, confirmed: 0, available: 0 }), [classes]);


  function activateStudent() {
    const trimmed = studentInput.trim();
    if (!STUDENT_ID_PATTERN.test(trimmed)) {
      showToast({ type: 'error', title: 'Check the student ID', message: 'Use exactly 17 English digits. The value is kept as text so leading zeroes are preserved.' });
      return;
    }
    try {
      localStorage.setItem(STUDENT_STORAGE_KEY, trimmed);
    } catch {
      showToast({ type: 'info', title: 'Browser storage unavailable', message: 'Your ID is active for this page, but may not be restored after refresh.' });
    }
    setStudentInput(trimmed);
    setActiveStudentId(trimmed);
    showToast({ type: 'success', title: 'Student ID selected', message: 'Your applications will now appear in My Applications.' });
  }

  async function applyToClass(admissionClass: AdmissionClass) {
    if (!activeStudentId) {
      showToast({ type: 'info', title: 'Student ID needed', message: 'Enter and use the student birth-registration number before applying.' });
      return;
    }
    if (admissionClass.availableSeats <= 0) return;
    setBusyAction(`apply:${admissionClass.id}`);
    try {
      await api.apply(admissionClass.id, activeStudentId);
      showToast({ type: 'success', title: 'Seat held successfully', message: `A seat in ${admissionClass.name} is temporarily reserved for the configured hold duration.` });
      await refreshAll(true);
    } catch (error) {
      // If the response was lost after the server committed, reconcile from the source of truth.
      if (error instanceof ApiError && error.code === 'NETWORK_ERROR') {
        const recoveredHolds = await refreshHolds(activeStudentId, true);
        await refreshClasses(true);
        const recovered = recoveredHolds.some(item => item.classId === admissionClass.id && item.status === 'HELD');
        showToast({
          type: recovered ? 'success' : 'error',
          title: recovered ? 'Application recovered' : 'Connection interrupted',
          message: recovered ? 'Your application was found on the server.' : 'We could not confirm whether the application was saved. Refresh your applications before trying again.',
        });
      } else {
        showToast({ type: 'error', title: 'Application not created', message: friendlyError(error) });
      }
      await refreshAll(true);
    } finally {
      setBusyAction(null);
    }
  }

  async function confirmAdmission(hold: AdmissionHold) {
    if (!activeStudentId) return;
    setBusyAction(`confirm:${hold.holdId}`);
    try {
      await api.confirm(hold.holdId, activeStudentId);
      showToast({ type: 'success', title: 'Admission confirmed', message: 'The assignment simulates a successful fee payment. No real payment was taken.' });
    } catch (error) {
      if (error instanceof ApiError && error.code === 'NETWORK_ERROR') {
        const current = await refreshHolds(activeStudentId, true);
        const recovered = current.some(item => item.holdId === hold.holdId && item.status === 'CONFIRMED');
        showToast({
          type: recovered ? 'success' : 'error',
          title: recovered ? 'Confirmation recovered' : 'Could not confirm admission',
          message: recovered ? 'The server shows that this admission is confirmed.' : 'The connection was interrupted. Refresh My Applications before trying again.',
        });
      } else {
        showToast({ type: 'error', title: 'Could not confirm admission', message: friendlyError(error) });
      }
    } finally {
      await refreshAll(true);
      setBusyAction(null);
    }
  }

  async function cancelApplication(hold: AdmissionHold) {
    if (!activeStudentId) return;
    setBusyAction(`cancel:${hold.holdId}`);
    try {
      await api.cancel(hold.holdId, activeStudentId);
      showToast({ type: 'success', title: 'Application cancelled', message: 'The held seat has been released for another student.' });
    } catch (error) {
      if (error instanceof ApiError && error.code === 'NETWORK_ERROR') {
        const current = await refreshHolds(activeStudentId, true);
        const recovered = current.some(item => item.holdId === hold.holdId && item.status === 'CANCELLED');
        showToast({
          type: recovered ? 'success' : 'error',
          title: recovered ? 'Cancellation recovered' : 'Could not cancel application',
          message: recovered ? 'The server shows that this application is cancelled.' : 'The connection was interrupted. Refresh My Applications before trying again.',
        });
      } else {
        showToast({ type: 'error', title: 'Could not cancel application', message: friendlyError(error) });
      }
    } finally {
      await refreshAll(true);
      setBusyAction(null);
    }
  }

  async function createClass(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const parsedSeats = Number(totalSeats);
    if (!className.trim() || !Number.isInteger(parsedSeats) || parsedSeats <= 0) {
      showToast({ type: 'error', title: 'Check the class details', message: 'Enter a class name and a positive whole number of seats.' });
      return;
    }
    setCreatingClass(true);
    try {
      const created = await api.createClass(className.trim(), parsedSeats);
      setClassName('');
      setTotalSeats('120');
      showToast({ type: 'success', title: 'Class and shift created', message: `${created.name} is now available for applications.` });
      await refreshClasses(true);
      setView('parent');
    } catch (error) {
      showToast({ type: 'error', title: 'Could not create class', message: friendlyError(error) });
    } finally {
      setCreatingClass(false);
    }
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <a className="brand" href="#top" aria-label="Northstar Academy home">
          <span className="brand-mark"><GraduationCap size={22} strokeWidth={2.1} /></span>
          <span className="brand-copy"><strong>Northstar Academy</strong><small>ADMISSIONS PORTAL</small></span>
        </a>

        <nav className="main-nav" aria-label="Main navigation">
          <button className={`nav-item ${view === 'parent' ? 'active' : ''}`} aria-current={view === 'parent' ? 'page' : undefined} onClick={() => setView('parent')}>
            <LayoutDashboard size={16} /> Admission portal
          </button>
          <button className={`nav-item ${view === 'admin' ? 'active' : ''}`} aria-current={view === 'admin' ? 'page' : undefined} onClick={() => setView('admin')}>
            <Settings2 size={16} /> School admin
          </button>
        </nav>

        <div className="topbar-meta">
          <span className={`service-status ${apiOnline ? 'online' : 'offline'}`}>
            {apiOnline ? <Wifi size={14} /> : <WifiOff size={14} />}
            <span>{apiOnline ? 'Service online' : 'Service unavailable'}</span>
          </span>
          <button className="refresh-button" onClick={() => void refreshAll()} disabled={refreshing} aria-label="Refresh data">
            <RefreshCw size={16} className={refreshing ? 'spin' : ''} />
          </button>
          <div className="avatar">NA</div>
        </div>
      </header>

      {!apiOnline && (
        <div className="service-banner" role="alert">
          <CircleAlert size={17} /> The admission service cannot be reached. Your entered ID is kept locally; changes will resume when the service is available.
          <button onClick={() => void refreshAll()}><RefreshCw size={14} /> Retry</button>
        </div>
      )}

      <main id="top" className="page-container">
        {view === 'parent' ? (
          <>
            <section className="hero-panel">
              <div className="hero-content">
                <span className="eyebrow"><span className="eyebrow-dot" /> ADMISSIONS 2026–27</span>
                <h1>Your next chapter<br /><em>starts here.</em></h1>
                <p>A clear, simple way to find your class, reserve a seat and complete your admission.</p>
                <div className="hero-actions">
                  <button className="button button-gold" onClick={() => document.getElementById('classes-heading')?.scrollIntoView({ behavior: 'smooth' })}>
                    Explore available classes <ArrowRight size={16} />
                  </button>
                  <span className="hero-note"><ShieldCheck size={15} /> First come, first served</span>
                </div>
              </div>
              <div className="hero-art" aria-hidden="true">
                <div className="hero-orbit orbit-one" />
                <div className="hero-orbit orbit-two" />
                <div className="hero-book"><BookOpen size={76} strokeWidth={1.1} /></div>
                <div className="hero-spark spark-one">✦</div>
                <div className="hero-spark spark-two">✧</div>
                <div className="hero-year">EST. 1998</div>
              </div>
              <div className="hero-bottom-line"><span>LEARN WITH PURPOSE</span><span>GROW WITH CONFIDENCE</span><span>MAKE YOUR MARK</span></div>
            </section>

            <section className="metric-grid" aria-label="Admission statistics">
              <MetricCard icon={<School size={18} />} label="Class & shift pools" value={formatNumber(classes.length)} foot="Open admission groups" accent="blue" />
              <MetricCard icon={<Users size={18} />} label="Seats available" value={formatNumber(summary.available)} foot="Ready to be reserved" accent="green" />
              <MetricCard icon={<Clock3 size={18} />} label="Temporarily held" value={formatNumber(summary.held)} foot="Awaiting fee confirmation" accent="amber" />
              <MetricCard icon={<BadgeCheck size={18} />} label="Confirmed" value={formatNumber(summary.confirmed)} foot="Successfully admitted" accent="violet" />
            </section>

            <section className="student-panel">
              <div className="student-icon"><ShieldCheck size={21} /></div>
              <div className="student-copy">
                <strong>Student identification</strong>
                <span>Enter the 17-digit birth-registration number to manage applications.</span>
              </div>
              <form className="student-form" onSubmit={(event) => { event.preventDefault(); activateStudent(); }}>
                <label className="sr-only" htmlFor="student-id">Student birth-registration number</label>
                <input
                  id="student-id"
                  inputMode="numeric"
                  autoComplete="off"
                  maxLength={17}
                  placeholder="17-digit student ID"
                  value={studentInput}
                  onChange={(event) => setStudentInput(event.target.value.replace(/[^0-9]/g, '').slice(0, 17))}
                  aria-describedby="student-id-help"
                />
                <button className="button button-dark" type="submit"><Check size={16} /> Use this ID</button>
              </form>
              <small id="student-id-help" className="student-help">{activeStudentId ? `Active ID: •••••••••••••${activeStudentId.slice(-4)}` : 'Your ID is used to retrieve your applications.'}</small>
            </section>
            <p className="demo-notice"><CircleAlert size={14} /> Demo notice: this assignment does not include login or identity verification. The student ID is an identifier, not secure authentication.</p>

            <div className="content-grid">
              <section className="classes-section">
                <div className="section-heading">
                  <div>
                    <span className="section-kicker">FIND YOUR PLACE</span>
                    <h2 id="classes-heading">Classes & shifts</h2>
                    <p>Each class and shift has its own limited seat capacity.</p>
                  </div>
                  <span className="live-update"><span /> Updates every 5 seconds</span>
                </div>
                {classesLoading ? (
                  <div className="loading-card"><LoaderCircle className="spin" size={22} /> Loading classes…</div>
                ) : classes.length === 0 && classesError ? (
                  <div className="empty-state" role="alert">
                    <span className="empty-icon"><CircleAlert size={28} /></span>
                    <h3>Classes are temporarily unavailable</h3>
                    <p>{classesError}</p>
                    <button className="button button-outline" onClick={() => void refreshClasses()}>Try again <RefreshCw size={15} /></button>
                  </div>
                ) : classes.length === 0 ? (
                  <div className="empty-state">
                    <span className="empty-icon"><BookOpen size={28} /></span>
                    <h3>No classes published yet</h3>
                    <p>The school admin has not created any admission groups. Please check again soon.</p>
                    <button className="button button-outline" onClick={() => setView('admin')}>Open admin area <ChevronRight size={16} /></button>
                  </div>
                ) : (
                  <div className="class-grid">
                    {classes.map((item, index) => (
                      <ClassCard
                        key={item.id}
                        admissionClass={item}
                        index={index}
                        active={Boolean(activeStudentId)}
                        busy={busyAction === `apply:${item.id}`}
                        onApply={() => void applyToClass(item)}
                      />
                    ))}
                  </div>
                )}
              </section>

              <aside className="applications-section">
                <div className="section-heading compact-heading">
                  <div>
                    <span className="section-kicker">YOUR ADMISSION JOURNEY</span>
                    <h2>My applications <span className="heading-count">{holds.length}</span></h2>
                  </div>
                  <button className="subtle-icon-button" onClick={() => void refreshHolds()} aria-label="Refresh applications"><RefreshCw size={16} /></button>
                </div>
                {!activeStudentId ? (
                  <div className="application-empty">
                    <div className="empty-illustration"><Users size={28} /></div>
                    <strong>Your applications live here</strong>
                    <p>Use your student ID above to see current holds and admission status.</p>
                  </div>
                ) : holdsLoading ? (
                  <div className="application-empty" role="status">
                    <LoaderCircle size={22} className="spin" />
                    <strong>Loading your applications…</strong>
                  </div>
                ) : holds.length === 0 ? (
                  <div className="application-empty">
                    <div className="empty-illustration"><BookOpen size={28} /></div>
                    <strong>No applications yet</strong>
                    <p>When you reserve a seat, it will appear here with a live countdown.</p>
                  </div>
                ) : (
                  <div className="application-list">
                    {holds.map(item => (
                      <ApplicationCard
                        key={item.holdId}
                        hold={item}
                        busy={busyAction === `confirm:${item.holdId}` || busyAction === `cancel:${item.holdId}`}
                        onConfirm={() => void confirmAdmission(item)}
                        onCancel={() => void cancelApplication(item)}
                      />
                    ))}
                  </div>
                )}
                <div className="application-footnote"><Clock3 size={14} /> Unpaid seat holds expire automatically.</div>
              </aside>
            </div>
          </>
        ) : (
          <AdminView
            classes={classes}
            className={className}
            totalSeats={totalSeats}
            creatingClass={creatingClass}
            onNameChange={setClassName}
            onSeatsChange={setTotalSeats}
            onSubmit={createClass}
            onBack={() => setView('parent')}
          />
        )}

        <footer className="footer">
          <div className="footer-brand"><span className="brand-mark small"><GraduationCap size={17} /></span><strong>Northstar Academy</strong></div>
          <span>School Admission System <i /> Built for a simpler admission experience</span>
          <span className="footer-secure"><ShieldCheck size={14} /> Seat availability enforced by server</span>
        </footer>
      </main>

      {toast && <ToastView toast={toast} onClose={() => setToast(null)} />}
    </div>
  );
}

function MetricCard({ icon, label, value, foot, accent }: { icon: ReactNode; label: string; value: string; foot: string; accent: string }) {
  return (
    <article className={`metric-card metric-${accent}`}>
      <div className="metric-top"><span className="metric-icon">{icon}</span><span className="metric-indicator"><Activity size={13} /> LIVE</span></div>
      <span className="metric-label">{label}</span>
      <strong className="metric-value">{value}</strong>
      <span className="metric-foot">{foot}</span>
    </article>
  );
}

function ClassCard({ admissionClass, index, active, busy, onApply }: {
  admissionClass: AdmissionClass;
  index: number;
  active: boolean;
  busy: boolean;
  onApply: () => void;
}) {
  const available = admissionClass.availableSeats > 0;
  const palette = ['class-art-blue', 'class-art-sand', 'class-art-lilac', 'class-art-green'][index % 4];
  return (
    <article className="class-card">
      <div className={`class-card-art ${palette}`}>
        <div className="class-art-mark">{index % 3 === 0 ? <BookOpen size={29} /> : index % 3 === 1 ? <GraduationCap size={32} /> : <School size={29} />}</div>
        <span className="class-type">ADMISSION GROUP {String(index + 1).padStart(2, '0')}</span>
        <span className={`availability-tag ${available ? 'available' : 'full'}`}><span />{available ? 'Applications open' : 'Currently full'}</span>
      </div>
      <div className="class-card-body">
        <div className="class-card-title-row">
          <div><span className="card-overline">CLASS & SHIFT</span><h3>{admissionClass.name}</h3></div>
          <span className="class-seat-count"><strong>{formatNumber(admissionClass.availableSeats)}</strong><small>available</small></span>
        </div>
        <SeatBar admissionClass={admissionClass} />
        <div className="class-stats">
          <span><i className="legend-dot confirmed-dot" /> {formatNumber(admissionClass.confirmedSeats)} confirmed</span>
          <span><i className="legend-dot held-dot" /> {formatNumber(admissionClass.heldSeats)} held</span>
          <span><i className="legend-dot available-dot" /> {formatNumber(admissionClass.availableSeats)} free</span>
        </div>
        <button className={`button class-apply ${available ? 'button-dark' : 'button-disabled'}`} onClick={onApply} disabled={!available || !active || busy}>
          {busy ? <LoaderCircle size={16} className="spin" /> : <>{available ? 'Apply for a seat' : 'No seats available'} <ArrowRight size={16} /></>}
        </button>
        {!active && <small className="class-hint">Enter your student ID above to apply.</small>}
      </div>
    </article>
  );
}

function ApplicationCard({ hold, busy, onConfirm, onCancel }: {
  hold: AdmissionHold;
  busy: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const remaining = useCountdown(hold.expiresAt);
  const effectiveStatus: HoldStatus = hold.status === 'HELD' && remaining <= 0 ? 'EXPIRED' : hold.status;
  const isHeld = effectiveStatus === 'HELD';
  return (
    <article className="application-card">
      <div className="application-card-top">
        <div className="application-class-icon"><BookOpen size={17} /></div>
        <span className={statusClass(effectiveStatus)}><span />{statusLabel(effectiveStatus)}</span>
      </div>
      <h3>{hold.className}</h3>
      <div className="application-meta"><span>Application ID</span><code>{hold.holdId.slice(0, 9)}…{hold.holdId.slice(-5)}</code></div>
      {isHeld && <div className="hold-countdown-row"><span>Complete within</span><Countdown expiresAt={hold.expiresAt} /></div>}
      {effectiveStatus === 'CONFIRMED' && <div className="confirmed-message"><CheckCircle2 size={16} /> Your seat is confirmed. Welcome aboard!</div>}
      {effectiveStatus === 'EXPIRED' && <div className="expired-message"><Clock3 size={15} /> This hold has expired. You may apply again if seats are available.</div>}
      {isHeld && (
        <div className="application-actions">
          <button className="button button-gold button-pay" onClick={onConfirm} disabled={busy || remaining <= 0}>
            {busy ? <LoaderCircle size={15} className="spin" /> : <><CreditCard size={15} /> Simulate fee payment</>}
          </button>
          <button className="button button-quiet" onClick={onCancel} disabled={busy}><X size={15} /> Cancel</button>
        </div>
      )}
    </article>
  );
}

function AdminView({ classes, className, totalSeats, creatingClass, onNameChange, onSeatsChange, onSubmit, onBack }: {
  classes: AdmissionClass[];
  className: string;
  totalSeats: string;
  creatingClass: boolean;
  onNameChange: (value: string) => void;
  onSeatsChange: (value: string) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  onBack: () => void;
}) {
  const available = classes.reduce((sum, item) => sum + item.availableSeats, 0);
  const confirmed = classes.reduce((sum, item) => sum + item.confirmedSeats, 0);
  return (
    <>
      <section className="admin-hero">
        <div><span className="eyebrow"><span className="eyebrow-dot" /> SCHOOL ADMINISTRATION</span><h1>Manage admissions<br /><em>with clarity.</em></h1><p>Create a class and shift pool, then monitor its live seat allocation.</p></div>
        <div className="admin-hero-icon"><Settings2 size={60} strokeWidth={1.2} /><Sparkles className="admin-spark" size={22} /></div>
      </section>
      <div className="admin-layout">
        <section className="admin-form-card">
          <div className="admin-card-heading"><span className="admin-icon"><Plus size={20} /></span><div><span className="section-kicker">NEW ADMISSION POOL</span><h2>Create class & shift</h2></div></div>
          <p className="admin-intro">Each class and shift is an independent seat pool. Use a clear name parents will recognize.</p>
          <form onSubmit={onSubmit} className="admin-form">
            <label htmlFor="class-name">Class and shift name</label>
            <input id="class-name" value={className} onChange={event => onNameChange(event.target.value)} placeholder="e.g. Class 6 - Morning Shift" maxLength={100} required />
            <div className="field-hint">Up to 100 characters. Duplicate names are allowed.</div>
            <label htmlFor="total-seats">Total seats</label>
            <input id="total-seats" type="number" min="1" step="1" value={totalSeats} onChange={event => onSeatsChange(event.target.value)} required />
            <div className="field-hint">A positive whole number. Capacity cannot be exceeded.</div>
            <button className="button button-dark admin-submit" type="submit" disabled={creatingClass}>
              {creatingClass ? <LoaderCircle size={16} className="spin" /> : <><Plus size={17} /> Create admission group</>}
            </button>
          </form>
          <div className="admin-form-note"><CircleAlert size={15} /> Admin tools are intentionally open for this take-home demo. Production use requires administrator authentication and authorization.</div>
        </section>

        <section className="admin-overview-card">
          <div className="section-heading admin-overview-heading"><div><span className="section-kicker">LIVE OVERVIEW</span><h2>Admission pools</h2></div><span className="live-update"><span /> Live</span></div>
          <div className="admin-kpis"><div><strong>{formatNumber(classes.length)}</strong><span>Class groups</span></div><div><strong>{formatNumber(available)}</strong><span>Available seats</span></div><div><strong>{formatNumber(confirmed)}</strong><span>Confirmed</span></div></div>
          <div className="admin-pool-list">
            {classes.length === 0 ? <div className="admin-empty"><BookOpen size={22} /><span>No admission pools created yet.</span></div> : classes.map(item => (
              <div className="admin-pool-item" key={item.id}>
                <div className="admin-pool-name"><strong>{item.name}</strong><small>{formatNumber(item.totalSeats)} total seats</small></div>
                <div className="admin-pool-available"><strong>{formatNumber(item.availableSeats)}</strong><small>available</small></div>
                <div className="admin-pool-progress"><span style={{ width: `${Math.min(100, ((item.heldSeats + item.confirmedSeats) / item.totalSeats) * 100)}%` }} /></div>
              </div>
            ))}
          </div>
          <button className="button button-outline full-width" onClick={onBack}>Back to parent portal <ArrowRight size={15} /></button>
        </section>
      </div>
    </>
  );
}

export default App;
