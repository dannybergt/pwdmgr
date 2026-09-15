import { useEffect, useState } from "react";
import { onUnauthorized } from "./api/client";
import { auth } from "./api/endpoints";
import type { Me } from "./api/types";
import { navigate, useRoute } from "./app/router";
import { LoginPage } from "./pages/LoginPage";
import { UnlockPage } from "./pages/UnlockPage";
import { VaultPage } from "./pages/VaultPage";
import * as session from "./session/vaultSession";

/**
 * Screen selection follows two facts: is there a server session (cookie, checked via /auth/me)
 * and is the vault unlocked (in-memory key). A 401 from any call drops both.
 */
export function App() {
  const route = useRoute();
  const [me, setMe] = useState<Me | null | undefined>(undefined);
  const [unlocked, setUnlocked] = useState(session.current());

  useEffect(() => session.subscribe(() => setUnlocked(session.current())), []);

  useEffect(() => {
    auth.me().then(setMe, () => setMe(null));
  }, []);

  useEffect(
    () =>
      onUnauthorized(() => {
        session.lock();
        setMe(null);
        navigate("/login");
      }),
    []
  );

  useEffect(() => {
    const touch = () => session.touch();
    for (const type of ["mousemove", "keydown", "click", "touchstart"]) {
      window.addEventListener(type, touch, { passive: true });
    }
    return () => {
      for (const type of ["mousemove", "keydown", "click", "touchstart"]) {
        window.removeEventListener(type, touch);
      }
    };
  }, []);

  useEffect(() => {
    if (me === undefined) {
      return;
    }
    const target = me === null ? "/login" : unlocked ? "/vault" : "/unlock";
    if (route !== target) {
      navigate(target);
    }
  }, [me, unlocked, route]);

  if (me === undefined) {
    return <main className="shell narrow"><p className="muted">Loading…</p></main>;
  }
  if (me === null) {
    return <LoginPage onLoggedIn={setMe} />;
  }
  if (!unlocked) {
    return (
      <UnlockPage
        me={me}
        onUnlocked={session.unlock}
        onLogout={() => {
          session.lock();
          setMe(null);
        }}
      />
    );
  }
  return (
    <VaultPage
      state={unlocked}
      onLock={session.lock}
      onLogout={() => {
        session.lock();
        setMe(null);
      }}
    />
  );
}
