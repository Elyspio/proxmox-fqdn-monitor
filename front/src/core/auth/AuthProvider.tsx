import { type ReactNode, createContext, useContext, useEffect, useMemo } from "react";
import { AuthProvider as OidcProvider, useAuth } from "react-oidc-context";
import { WebStorageStateStore } from "oidc-client-ts";
import { runtimeConfig } from "@/config/runtime";
import { setAccessToken } from "@/core/api/client";

export interface AppAuth {
	isLoading: boolean;
	isAuthenticated: boolean;
	name: string | null;
	signIn: () => void;
	signOut: () => void;
	error: string | null;
}

const AppAuthContext = createContext<AppAuth | null>(null);

/**
 * Bridges react-oidc-context to the rest of the app: it keeps the axios bearer token in sync
 * and exposes a small surface so components never touch oidc-client-ts directly.
 *
 * Role checking deliberately lives on the server. The frontend does not decode the token to
 * decide what it may show — it asks the API and renders the refusal, so the role name has a
 * single definition instead of one on each side that can drift apart.
 */
function AppAuthBridge({ children }: { children: ReactNode }) {
	const auth = useAuth();

	useEffect(() => {
		setAccessToken(auth.user?.access_token ?? null);
	}, [auth.user]);

	// Silent renewal replaces the user object; without this the axios token goes stale
	// and every request starts failing with 401 after the first token lifetime.
	useEffect(() => {
		return auth.events.addUserLoaded((user) => setAccessToken(user.access_token));
	}, [auth.events]);

	const value = useMemo<AppAuth>(
		() => ({
			isLoading: auth.isLoading,
			isAuthenticated: auth.isAuthenticated,
			name: auth.user?.profile.name ?? auth.user?.profile.preferred_username ?? null,
			signIn: () => void auth.signinRedirect(),
			signOut: () => void auth.signoutRedirect(),
			error: auth.error?.message ?? null,
		}),
		[auth],
	);

	return <AppAuthContext.Provider value={value}>{children}</AppAuthContext.Provider>;
}

export function AppAuthProvider({ children }: { children: ReactNode }) {
	const oidc = runtimeConfig.oauth;

	return (
		<OidcProvider
			authority={oidc.authority}
			client_id={oidc.client_id}
			redirect_uri={oidc.redirect_uri}
			post_logout_redirect_uri={oidc.post_logout_redirect_uri}
			response_type={oidc.response_type}
			scope={oidc.scope}
			automaticSilentRenew
			userStore={new WebStorageStateStore({ store: window.localStorage })}
			onSigninCallback={() => {
				// Drop the authorization code from the address bar so a refresh does not
				// replay a one-time code and fail.
				window.history.replaceState({}, document.title, window.location.pathname);
			}}
		>
			<AppAuthBridge>{children}</AppAuthBridge>
		</OidcProvider>
	);
}

export function useAppAuth(): AppAuth {
	const context = useContext(AppAuthContext);
	if (!context) throw new Error("useAppAuth must be used inside AppAuthProvider");
	return context;
}
