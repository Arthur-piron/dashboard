import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Routes, Route } from 'react-router-dom'
import './index.css'
import App from './App.jsx'
import Login from './login.jsx'
import Register from './register.jsx'
import Confirm from './confirm.jsx'
import OidcCallback from './oidcCallback.jsx'
import Services from './services.jsx'
import Admin from './admin.jsx'
import { AppLayout, AuthLayout, RequireAdmin } from './layouts.jsx'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        <Route element={<AuthLayout />}>
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />
          <Route path="/confirm" element={<Confirm />} />
          <Route path="/auth/callback" element={<OidcCallback />} />
        </Route>
        <Route element={<AppLayout />}>
          <Route path="/" element={<App />} />
          <Route path="/services" element={<Services />} />
          <Route
            path="/admin"
            element={
              <RequireAdmin>
                <Admin />
              </RequireAdmin>
            }
          />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  </StrictMode>,
)
