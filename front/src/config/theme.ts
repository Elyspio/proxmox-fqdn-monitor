import { createTheme } from "@mui/material";

/**
 * Addresses, hostnames and zone names are compared character by character, so everything
 * that carries one is rendered monospaced — a transposed digit has to be visible.
 */
export const monospace = '"JetBrains Mono Variable", ui-monospace, monospace';

export const theme = createTheme({
	palette: {
		mode: "dark",
		primary: { main: "#7aa2f7" },
		secondary: { main: "#bb9af7" },
		background: { default: "#12141c", paper: "#1a1d29" },
		success: { main: "#9ece6a" },
		warning: { main: "#e0af68" },
		error: { main: "#f7768e" },
	},
	typography: {
		fontFamily: '"Space Grotesk Variable", system-ui, sans-serif',
	},
	shape: { borderRadius: 10 },
	components: {
		MuiTableCell: {
			styleOverrides: {
				root: { borderColor: "rgba(255,255,255,0.06)" },
			},
		},
	},
});
