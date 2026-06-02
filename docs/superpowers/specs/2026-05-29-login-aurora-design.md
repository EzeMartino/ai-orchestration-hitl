# Especificación de Diseño: Login con Glassmorphism & Aurora

Este documento describe la especificación técnica y de diseño para revitalizar la página de inicio de sesión (`AuthPage`), resolviendo un problema de visualización y aplicando un fondo dinámico con efecto Aurora y Glassmorphism (cristal esmerilado) premium.

## 1. Contexto y Objetivos

*   **Problema actual:** La pantalla de login muestra un fondo gris claro plano que se siente vacío e interrumpe la estética premium e inmersiva de la Sala de Control. Esto se debe a un error de sintaxis en `AuthPage.css` (`radial-gradient(135deg, ...)`), lo que hace que el navegador descarte el fondo oscuro diseñado originalmente.
*   **Solución propuesta:** 
    *   Corregir la sintaxis del degradado del contenedor para establecer un espacio profundo oscuro.
    *   Añadir tres auroras de colores orgánicos flotando suavemente de fondo usando CSS acelerado por hardware para lograr dinamismo visual sin impacto en el rendimiento.
    *   Optimizar la tarjeta de login (`.auth-card`) mejorando el contraste de sus textos y aplicando un efecto de vidrio esmerilado (Glassmorphism) más definido para una legibilidad superior (cumpliendo con pautas de accesibilidad WCAG AA).

---

## 2. Arquitectura de Componentes y Cambios

### 2.1. Estructura HTML (`AuthPage.tsx`)

Inyectaremos una sección dedicada al fondo de la aurora justo dentro del contenedor principal y antes de la tarjeta. Los blobs decorativos tendrán el atributo `aria-hidden="true"` para que los lectores de pantalla los ignoren por completo.

Ruta del archivo: [AuthPage.tsx](file:///c:/Repositories/ai-orchestration-hitl/frontend/src/components/Auth/AuthPage.tsx)

```tsx
return (
  <div className="auth-page-container">
    {/* Auroras de fondo decorativas */}
    <div className="aurora-bg" aria-hidden="true">
      <div className="aurora-blob blob-1"></div>
      <div className="aurora-blob blob-2"></div>
      <div className="aurora-blob blob-3"></div>
    </div>

    {/* Tarjeta de Autenticación */}
    <div className="auth-card">
      {/* ... contenido existente del formulario ... */}
    </div>
  </div>
);
```

### 2.2. Diseño Estilizado (`AuthPage.css`)

Ruta del archivo: [AuthPage.css](file:///c:/Repositories/ai-orchestration-hitl/frontend/src/components/Auth/AuthPage.css)

#### Fondo Base y Contenedor Principal
*   **Corrección:** Cambiar el fondo del contenedor para usar un degradado radial CSS válido que transicione desde un azul medianoche profundo a un gris oscuro espacial.
*   **Estilo:**
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
      overflow: hidden; /* Evita barras de scroll por los blobs difuminados */
    }
    ```

#### Capa Aurora y Blobs Decorativos
*   La capa contenedora `aurora-bg` se posicionará de manera absoluta y ocupará todo el ancho/alto detrás de la tarjeta.
*   Los blobs utilizarán colores contrastantes pero suaves (cian, violeta e índigo) con filtros de desenfoque masivos.
*   **Estilos:**
    ```css
    .aurora-bg {
      position: absolute;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
      overflow: hidden;
      z-index: 1;
      pointer-events: none; /* Asegura que no interfiera con clics */
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
    ```

#### Animaciones de Órbita Lenta
*   Para evitar el impacto en el rendimiento, las animaciones usarán únicamente `transform` (translación y escala sutiles) que no gatillan reflows ni repaints y se ejecutan directamente en la GPU.
*   **Animaciones:**
    ```css
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
    ```

#### Perfeccionamiento de la Tarjeta Glassmorphic
*   **Foco en Legibilidad:** Aumentaremos el contraste de los textos ajustando el fondo de la tarjeta de login para que sea un gris-azul oscuro translúcido más denso. Esto creará una separación perfecta de los colores de la aurora que flotan detrás.
*   **Estilo:**
    ```css
    .auth-card {
      width: 100%;
      max-width: 440px;
      backdrop-filter: blur(24px) saturate(180%);
      -webkit-backdrop-filter: blur(24px) saturate(180%); /* Soporte Safari */
      background-color: rgba(11, 17, 34, 0.65); /* Más denso y oscuro para contraste óptimo */
      border: 1px solid rgba(255, 255, 255, 0.08);
      box-shadow: 
        0 4px 30px rgba(0, 0, 0, 0.4),
        inset 0 1px 1px rgba(255, 255, 255, 0.05); /* Bisel interno sutil */
      border-radius: 20px;
      padding: 2.5rem;
      box-sizing: border-box;
      display: flex;
      flex-direction: column;
      color: #f8fafc;
      z-index: 2; /* Por encima de la aurora */
      animation: fadeIn 0.6s cubic-bezier(0.16, 1, 0.3, 1) forwards;
    }
    ```

---

## 3. Plan de Verificación

### 3.1. Pruebas Visuales y de UX
*   **Renderizado de Fondo:** Verificar que el fondo de la pantalla ya no se vea gris claro, sino un azul oscuro profundo espacial.
*   **Efecto Aurora:** Validar que los tres blobs de color se carguen correctamente con bordes difusos (gracias al filtro de desenfoque de 120px) y que se muevan lentamente sin causar saltos visuales.
*   **Legibilidad del Texto:** Asegurar que los textos informativos ("Bienvenido de Nuevo", "Dirección de Correo Electrónico", etc.) tengan suficiente contraste respecto al fondo dinámico.
*   **Responsividad:** Probar en resoluciones móviles, tablets y monitores de escritorio para garantizar que los blobs decorativos no desborden la ventana y mantengan el foco centrado en la tarjeta.

### 3.2. Rendimiento
*   Monitorear en las herramientas de desarrollo del navegador que la animación no eleve el consumo de CPU ni provoque caídas de fotogramas, verificando que sólo se animen propiedades transformadas.
