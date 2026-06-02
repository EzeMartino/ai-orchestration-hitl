# Login Aurora Background Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a sleek, dynamic "Glassmorphism & Aurora" background with slowly floating fluid blobs on the Login page to achieve a state-of-the-art premium look.

**Architecture:** We will insert three decorative divs in the React component hierarchy representing high-blur colored circles. Using hardware-accelerated CSS animations (`transform` and `scale`), we will animate them along slow paths to prevent CPU/repaint overhead, and apply dense backdrop-filter glassmorphism on the main authentication card to achieve a modern premium blur with maximum accessibility and readability.

**Tech Stack:** React 19, TypeScript, Vanilla CSS3 (flexbox, transitions, keyframes, backdrop-filter)

---

### Task 1: Update AuthPage.tsx HTML/DOM Structure

**Files:**
- Modify: `c:/Repositories/ai-orchestration-hitl/frontend/src/components/Auth/AuthPage.tsx:62-67`

- [ ] **Step 1: Insert the decorative aurora backdrop layers in the DOM**
  We need to add the `.aurora-bg` container and its three `.aurora-blob` children before the `.auth-card` div inside `.auth-page-container` in `AuthPage.tsx`.

  ```tsx
  return (
    <div className="auth-page-container">
      {/* Auroras de fondo decorativas */}
      <div className="aurora-bg" aria-hidden="true">
        <div className="aurora-blob blob-1"></div>
        <div className="aurora-blob blob-2"></div>
        <div className="aurora-blob blob-3"></div>
      </div>

      <div className="auth-card">
  ```

- [ ] **Step 2: Verify code compiling and formatting**
  Verify that the JSX has no syntax errors and the project builds successfully.

- [ ] **Step 3: Commit structural JSX changes**
  ```bash
  git add frontend/src/components/Auth/AuthPage.tsx
  git commit -m "feat(auth): add structural HTML elements for aurora background blobs"
  ```

---

### Task 2: Write premium Glassmorphism & Aurora styles in AuthPage.css

**Files:**
- Modify: `c:/Repositories/ai-orchestration-hitl/frontend/src/components/Auth/AuthPage.css:1-26`

- [ ] **Step 1: Replace container and card styles, and define aurora blobs and keyframe animations**
  Modify `AuthPage.css` starting from line 1 to add the fixed base background, overflow: hidden, absolute-positioned aurora containers, colored high-blur blobs,Slow-floating keyframe animations, and dense high-contrast glassmorphic `.auth-card` styles.

  Replace the beginning of `AuthPage.css` with:
  ```css
  .auth-page-container {
    min-height: 100vh;
    display: flex;
    align-items: center;
    justify-content: center;
    background: radial-gradient(circle at 50% 50%, #0c152b 0%, #030712 100%);
    padding: 1.5rem;
    box-sizing: border-box;
    font-family: Inter, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
    position: relative;
    overflow: hidden;
  }

  /* Capa Aurora y Blobs Decorativos */
  .aurora-bg {
    position: absolute;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    overflow: hidden;
    z-index: 1;
    pointer-events: none;
  }

  .aurora-blob {
    position: absolute;
    border-radius: 50%;
    filter: blur(120px);
    opacity: 0.18;
    mix-blend-mode: screen;
    transition: all 0.5s ease;
  }

  .blob-1 {
    top: -10%;
    left: -10%;
    width: 500px;
    height: 500px;
    background: #06b6d4; /* Cian */
    animation: float-slow-1 25s infinite alternate ease-in-out;
  }

  .blob-2 {
    bottom: -15%;
    right: -10%;
    width: 600px;
    height: 600px;
    background: #6366f1; /* Índigo */
    animation: float-slow-2 30s infinite alternate ease-in-out;
  }

  .blob-3 {
    top: 30%;
    left: 35%;
    width: 400px;
    height: 400px;
    background: #ec4899; /* Rosa/Magenta */
    animation: float-slow-3 20s infinite alternate ease-in-out;
  }

  /* Animaciones de Órbita Lenta Aceleradas por Hardware */
  @keyframes float-slow-1 {
    0% { transform: translate(0, 0) scale(1); }
    50% { transform: translate(100px, 80px) scale(1.15); }
    100% { transform: translate(-50px, 120px) scale(0.9); }
  }

  @keyframes float-slow-2 {
    0% { transform: translate(0, 0) scale(1.1); }
    50% { transform: translate(-120px, -60px) scale(0.9); }
    100% { transform: translate(80px, 40px) scale(1.2); }
  }

  @keyframes float-slow-3 {
    0% { transform: translate(0, 0) scale(0.9); }
    50% { transform: translate(60px, -100px) scale(1.1); }
    100% { transform: translate(-80px, 50px) scale(0.95); }
  }

  .auth-card {
    width: 100%;
    max-width: 440px;
    backdrop-filter: blur(24px) saturate(180%);
    -webkit-backdrop-filter: blur(24px) saturate(180%);
    background-color: rgba(11, 17, 34, 0.65); /* Más denso y oscuro para contraste óptimo */
    border: 1px solid rgba(255, 255, 255, 0.08);
    box-shadow: 
      0 8px 32px 0 rgba(0, 0, 0, 0.37),
      inset 0 1px 1px rgba(255, 255, 255, 0.05);
    border-radius: 20px;
    padding: 2.5rem;
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    color: #f8fafc;
    z-index: 2; /* Por encima de la aurora */
    animation: fadeIn 0.5s cubic-bezier(0.16, 1, 0.3, 1) forwards;
  }
  ```

- [ ] **Step 2: Commit CSS changes**
  ```bash
  git add frontend/src/components/Auth/AuthPage.css
  git commit -m "style(auth): implement CSS aurora background blobs, hardware animations, and glassmorphic card contrast improvements"
  ```

---

### Task 3: Verify build and visual presentation

**Files:**
- Verify: `frontend/src/components/Auth/AuthPage.tsx`
- Verify: `frontend/src/components/Auth/AuthPage.css`

- [ ] **Step 1: Perform code validation and type check**
  Run local linting/compilation if available. Since it is a Vite React project, we can run typescript compiler or a build to make sure there are no issues.

- [ ] **Step 2: User Manual Verification**
  Advise the user to look at the running dev server on `http://localhost:5173` to see the gorgeous live background rendering and confirm they love the floating aurora blobs and glassmorphic visual treatment.
