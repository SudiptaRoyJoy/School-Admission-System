import { Component, type ErrorInfo, type ReactNode } from 'react';
import { CircleAlert, RefreshCw } from 'lucide-react';

interface Props { children: ReactNode; }
interface State { hasError: boolean; }

export default class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false };

  static getDerivedStateFromError(): State {
    return { hasError: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Keep diagnostics in the browser console; never expose stack traces to parents.
    console.error('Admission UI rendering error:', error, info.componentStack);
  }

  render() {
    if (this.state.hasError) {
      return (
        <main className="fatal-error">
          <span className="fatal-error-icon"><CircleAlert size={28} /></span>
          <h1>We hit an unexpected problem.</h1>
          <p>Your application data is stored on the admission server. Refresh the page to try again.</p>
          <button className="button button-dark" onClick={() => window.location.reload()}><RefreshCw size={16} /> Refresh page</button>
        </main>
      );
    }
    return this.props.children;
  }
}
