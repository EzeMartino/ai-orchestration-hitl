let activeHarness = null;

export function useState(initialValue) {
  return activeHarness.useState(initialValue);
}

export function useRef(initialValue) {
  return activeHarness.useRef(initialValue);
}

export function useCallback(callback, dependencies) {
  return activeHarness.useCallback(callback, dependencies);
}

export function useEffect(effect, dependencies) {
  return activeHarness.useEffect(effect, dependencies);
}

function dependenciesChanged(previous, next) {
  return !previous || !next || previous.length !== next.length ||
    previous.some((value, index) => !Object.is(value, next[index]));
}

export function renderHook(hook, { strictMode = true } = {}) {
  const slots = [];
  let cursor = 0;
  let current;
  let renderQueued = false;
  let disposed = false;

  const scheduleRender = () => {
    if (renderQueued || disposed) {
      return;
    }

    renderQueued = true;
    queueMicrotask(() => {
      renderQueued = false;
      if (!disposed) {
        render();
      }
    });
  };

  const dispatcher = {
    useState(initialValue) {
      const index = cursor++;
      if (!slots[index]) {
        slots[index] = {
          kind: "state",
          value: typeof initialValue === "function" ? initialValue() : initialValue,
        };
      }
      const setValue = (nextValue) => {
        const previousValue = slots[index].value;
        const resolvedValue = typeof nextValue === "function"
          ? nextValue(previousValue)
          : nextValue;
        if (!Object.is(previousValue, resolvedValue)) {
          slots[index].value = resolvedValue;
          scheduleRender();
        }
      };
      return [slots[index].value, setValue];
    },
    useRef(initialValue) {
      const index = cursor++;
      if (!slots[index]) {
        slots[index] = { kind: "ref", value: { current: initialValue } };
      }
      return slots[index].value;
    },
    useCallback(callback, dependencies) {
      const index = cursor++;
      const slot = slots[index];
      if (!slot || dependenciesChanged(slot.dependencies, dependencies)) {
        slots[index] = { kind: "callback", value: callback, dependencies };
      }
      return slots[index].value;
    },
    useEffect(effect, dependencies) {
      const index = cursor++;
      const slot = slots[index];
      if (!slot) {
        slots[index] = { kind: "effect", effect, dependencies, pending: true };
      } else if (dependenciesChanged(slot.dependencies, dependencies)) {
        slot.effect = effect;
        slot.dependencies = dependencies;
        slot.pending = true;
      }
    },
  };

  const commitEffects = () => {
    for (const slot of slots) {
      if (slot?.kind !== "effect" || !slot.pending) {
        continue;
      }
      slot.cleanup?.();
      slot.cleanup = slot.effect() ?? undefined;
      slot.pending = false;
    }
  };

  const render = () => {
    cursor = 0;
    activeHarness = dispatcher;
    try {
      current = hook();
    } finally {
      activeHarness = null;
    }
    commitEffects();
  };

  render();
  if (strictMode) {
    for (const slot of slots) {
      if (slot?.kind === "effect") {
        slot.cleanup?.();
        slot.cleanup = undefined;
        slot.pending = true;
      }
    }
    render();
  }

  return {
    result: {
      get current() {
        return current;
      },
    },
    async flush() {
      for (let index = 0; index < 8; index += 1) {
        await Promise.resolve();
      }
    },
    unmount() {
      disposed = true;
      for (const slot of slots) {
        if (slot?.kind === "effect") {
          slot.cleanup?.();
        }
      }
    },
  };
}
