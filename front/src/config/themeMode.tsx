import {
	createContext,
	useCallback,
	useContext,
	useEffect,
	useMemo,
	useState,
	type ReactNode,
} from "react";
import { CssBaseline, ThemeProvider } from "@mui/material";
import { createAppTheme, type PaletteMode } from "@/config/theme";

/** What the operator picked. "system" follows the OS; "light"/"dark" pin the palette. */
export type ThemeMode = "system" | "light" | "dark";

export const themeModes: readonly ThemeMode[] = ["system", "light", "dark"];

const STORAGE_KEY = "proxmox-ip-monitor.theme-mode";

export function isThemeMode(value: unknown): value is ThemeMode {
	return typeof value === "string" && (themeModes as readonly string[]).includes(value);
}

/** Reads the persisted preference, defaulting to "system" when absent or corrupt. */
export function readStoredMode(storage: Pick<Storage, "getItem"> | undefined): ThemeMode {
	return isThemeMode(storage?.getItem(STORAGE_KEY))
		? (storage!.getItem(STORAGE_KEY) as ThemeMode)
		: "system";
}

/** Collapses the preference and the OS setting into the palette that actually renders. */
export function resolvePaletteMode(mode: ThemeMode, systemPrefersDark: boolean): PaletteMode {
	if (mode === "system") return systemPrefersDark ? "dark" : "light";
	return mode;
}

interface ThemeModeContextValue {
	/** The stored preference, including "system". */
	mode: ThemeMode;
	/** The palette currently rendered — "system" already resolved against the OS. */
	resolved: PaletteMode;
	setMode: (mode: ThemeMode) => void;
}

const ThemeModeContext = createContext<ThemeModeContextValue | null>(null);

const darkMediaQuery = "(prefers-color-scheme: dark)";

/**
 * Owns the theme selection: the persisted preference, the live OS preference, and the resulting
 * MUI theme. It renders the {@link ThemeProvider} itself so callers only pick a mode, never a theme.
 */
export function ThemeModeProvider({ children }: { children: ReactNode }) {
	const [mode, setModeState] = useState<ThemeMode>(() =>
		readStoredMode(typeof window === "undefined" ? undefined : window.localStorage),
	);
	const [systemPrefersDark, setSystemPrefersDark] = useState<boolean>(
		() => typeof window !== "undefined" && window.matchMedia(darkMediaQuery).matches,
	);

	// While on "system", track OS theme changes so the UI flips without a reload.
	useEffect(() => {
		const query = window.matchMedia(darkMediaQuery);
		const onChange = (event: MediaQueryListEvent) => setSystemPrefersDark(event.matches);
		query.addEventListener("change", onChange);
		return () => query.removeEventListener("change", onChange);
	}, []);

	const setMode = useCallback((next: ThemeMode) => {
		setModeState(next);
		try {
			window.localStorage.setItem(STORAGE_KEY, next);
		} catch {
			// Private mode or disabled storage: the choice applies now but will not survive a reload.
		}
	}, []);

	const resolved = resolvePaletteMode(mode, systemPrefersDark);
	const theme = useMemo(() => createAppTheme(resolved), [resolved]);
	const value = useMemo<ThemeModeContextValue>(
		() => ({ mode, resolved, setMode }),
		[mode, resolved, setMode],
	);

	return (
		<ThemeModeContext.Provider value={value}>
			<ThemeProvider theme={theme}>
				<CssBaseline />
				{children}
			</ThemeProvider>
		</ThemeModeContext.Provider>
	);
}

export function useThemeMode(): ThemeModeContextValue {
	const context = useContext(ThemeModeContext);
	if (!context) throw new Error("useThemeMode must be used within a ThemeModeProvider");
	return context;
}
