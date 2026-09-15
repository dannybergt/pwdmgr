import { useEffect, useState } from "react";

/** Three routes, no library: /login, /unlock, /vault. */
export type Route = "/login" | "/unlock" | "/vault";

function normalise(path: string): Route {
  return path === "/unlock" || path === "/vault" ? path : "/login";
}

export function navigate(route: Route): void {
  if (window.location.pathname !== route) {
    window.history.pushState(null, "", route);
  }
  window.dispatchEvent(new PopStateEvent("popstate"));
}

export function useRoute(): Route {
  const [route, setRoute] = useState<Route>(() => normalise(window.location.pathname));
  useEffect(() => {
    const onPop = () => setRoute(normalise(window.location.pathname));
    window.addEventListener("popstate", onPop);
    return () => window.removeEventListener("popstate", onPop);
  }, []);
  return route;
}
