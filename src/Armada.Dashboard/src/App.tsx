import { BrowserRouter } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { LocaleProvider } from './context/LocaleContext';
import { ThemeProvider } from './context/ThemeContext';
import { WebSocketProvider } from './context/WebSocketContext';
import { NotificationProvider } from './context/NotificationContext';
import ProtectedRoute from './components/ProtectedRoute';
import Layout from './components/Layout';
import AppRoutes from './AppRoutes';
import './App.css';

export default function App() {
  return (
    <LocaleProvider>
      <ThemeProvider>
        <AuthProvider>
          <WebSocketProvider>
            <NotificationProvider>
              <BrowserRouter basename="/dashboard">
                <AppRoutes layout={<ProtectedRoute><Layout /></ProtectedRoute>} />
              </BrowserRouter>
            </NotificationProvider>
          </WebSocketProvider>
        </AuthProvider>
      </ThemeProvider>
    </LocaleProvider>
  );
}
