import React, { useState } from "react";
import { login, registerUser } from "../../services/api";
import "./AuthPage.css";

interface AuthPageProps {
  onLoginSuccess: (token: string) => void;
}

export default function AuthPage({ onLoginSuccess }: AuthPageProps) {
  const [isLogin, setIsLogin] = useState(true);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setSuccessMessage(null);

    // Validate inputs
    if (!email.trim() || !password.trim()) {
      setError("Please fill in all required fields.");
      return;
    }

    if (!isLogin && password !== confirmPassword) {
      setError("Passwords do not match.");
      return;
    }

    setIsLoading(true);
    try {
      if (isLogin) {
        const response = await login({ email: email.trim(), password });
        localStorage.setItem("auth_token", response.accessToken);
        onLoginSuccess(response.accessToken);
      } else {
        await registerUser({ email: email.trim(), password });
        setSuccessMessage("Registration successful! You can now log in.");
        setIsLogin(true);
        setPassword("");
        setConfirmPassword("");
      }
    } catch (err: any) {
      setError(err instanceof Error ? err.message : "Authentication failed.");
    } finally {
      setIsLoading(false);
    }
  };

  const handleToggleView = () => {
    setIsLogin(!isLogin);
    setError(null);
    setSuccessMessage(null);
    setPassword("");
    setConfirmPassword("");
  };

  return (
    <div className="auth-page-container">
      <div className="auth-card">
        <div className="auth-header">
          <div className="logo-icon">
            <svg
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <polygon points="12 2 2 7 12 12 22 7 12 2" />
              <polyline points="2 17 12 22 22 17" />
              <polyline points="2 12 12 17 22 12" />
            </svg>
          </div>
          <h1>{isLogin ? "Welcome Back" : "Create Account"}</h1>
          <p className="auth-subtitle">
            {isLogin
              ? "Sign in to access the Financial Control Room"
              : "Register to orchestrate secure agent workflows"}
          </p>
        </div>

        {error && (
          <div className="auth-message auth-error animate-fade-in">
            <svg className="alert-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <circle cx="12" cy="12" r="10" />
              <line x1="12" y1="8" x2="12" y2="12" />
              <line x1="12" y1="16" x2="12.01" y2="16" />
            </svg>
            <span>{error}</span>
          </div>
        )}

        {successMessage && (
          <div className="auth-message auth-success animate-fade-in">
            <svg className="alert-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
              <polyline points="22 4 12 14.01 9 11.01" />
            </svg>
            <span>{successMessage}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="auth-form">
          <div className="form-group">
            <label htmlFor="email">Email Address</label>
            <input
              id="email"
              type="email"
              placeholder="name@example.com"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={isLoading}
              required
              autoComplete="email"
            />
          </div>

          <div className="form-group">
            <label htmlFor="password">Password</label>
            <input
              id="password"
              type="password"
              placeholder="••••••••"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              disabled={isLoading}
              required
              autoComplete={isLogin ? "current-password" : "new-password"}
            />
          </div>

          {!isLogin && (
            <div className="form-group animate-slide-down">
              <label htmlFor="confirmPassword">Confirm Password</label>
              <input
                id="confirmPassword"
                type="password"
                placeholder="••••••••"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                disabled={isLoading}
                required
                autoComplete="new-password"
              />
            </div>
          )}

          <button type="submit" className="btn-primary" disabled={isLoading}>
            {isLoading ? (
              <span className="spinner"></span>
            ) : isLogin ? (
              "Sign In"
            ) : (
              "Register"
            )}
          </button>
        </form>

        <div className="auth-footer">
          <button onClick={handleToggleView} disabled={isLoading} className="btn-toggle">
            {isLogin
              ? "Don't have an account? Register"
              : "Already have an account? Sign In"}
          </button>
        </div>
      </div>
    </div>
  );
}
