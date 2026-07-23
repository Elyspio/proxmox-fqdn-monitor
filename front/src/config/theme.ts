import { createTheme, type Theme } from "@mui/material";

/**
 * Addresses, hostnames and zone names are compared character by character, so everything
 * that carries one is rendered monospaced — a transposed digit has to be visible.
 */
export const monospace = '"JetBrains Mono Variable", ui-monospace, monospace';

export type PaletteMode = "light" | "dark";

// Tokyo Night (dark) and its Day variant (light): the same hues tuned for each background,
// so an address keeps the same semantic colour whichever mode the operator is in.
const palettes = {
	dark: {
		primary: "#7aa2f7",
		secondary: "#bb9af7",
		background: { default: "#12141c", paper: "#1a1d29" },
		success: "#9ece6a",
		warning: "#e0af68",
		error: "#f7768e",
		tableBorder: "rgba(255,255,255,0.06)",
	},
	light: {
		primary: "#2e7de9",
		secondary: "#9854f1",
		background: { default: "#e6e7ee", paper: "#ffffff" },
		success: "#587539",
		warning: "#8c6c3e",
		error: "#f52a65",
		tableBorder: "rgba(0,0,0,0.08)",
	},
} as const;

export function createAppTheme(mode: PaletteMode): Theme {
	const colors = palettes[mode];
	return createTheme({
		palette: {
			mode,
			primary: { main: colors.primary },
			secondary: { main: colors.secondary },
			background: colors.background,
			success: { main: colors.success },
			warning: { main: colors.warning },
			error: { main: colors.error },
		},
		typography: {
			fontFamily: '"Space Grotesk Variable", system-ui, sans-serif',
		},
		shape: { borderRadius: 10 },
		components: {
			MuiTableCell: {
				styleOverrides: {
					root: { borderColor: colors.tableBorder },
				},
			},
		},
	});
}
