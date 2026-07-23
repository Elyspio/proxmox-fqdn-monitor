import { useState, type MouseEvent, type ReactNode } from "react";
import { IconButton, ListItemIcon, ListItemText, Menu, MenuItem, Tooltip } from "@mui/material";
import CheckIcon from "@mui/icons-material/Check";
import DarkModeIcon from "@mui/icons-material/DarkMode";
import LightModeIcon from "@mui/icons-material/LightMode";
import SettingsBrightnessIcon from "@mui/icons-material/SettingsBrightness";
import { useThemeMode, type ThemeMode } from "@/config/themeMode";

const options: { mode: ThemeMode; label: string; icon: ReactNode }[] = [
	{ mode: "system", label: "Système", icon: <SettingsBrightnessIcon fontSize="small" /> },
	{ mode: "light", label: "Clair", icon: <LightModeIcon fontSize="small" /> },
	{ mode: "dark", label: "Sombre", icon: <DarkModeIcon fontSize="small" /> },
];

export function ThemeToggle() {
	const { mode, setMode } = useThemeMode();
	const [anchor, setAnchor] = useState<HTMLElement | null>(null);

	const current = options.find((option) => option.mode === mode) ?? options[0];

	const open = (event: MouseEvent<HTMLElement>) => setAnchor(event.currentTarget);
	const close = () => setAnchor(null);
	const choose = (next: ThemeMode) => {
		setMode(next);
		close();
	};

	return (
		<>
			<Tooltip title="Thème">
				<IconButton
					size="small"
					color="inherit"
					onClick={open}
					aria-label="Changer de thème"
				>
					{current.icon}
				</IconButton>
			</Tooltip>
			<Menu anchorEl={anchor} open={Boolean(anchor)} onClose={close}>
				{options.map((option) => (
					<MenuItem
						key={option.mode}
						selected={option.mode === mode}
						onClick={() => choose(option.mode)}
					>
						<ListItemIcon>{option.icon}</ListItemIcon>
						<ListItemText>{option.label}</ListItemText>
						{option.mode === mode && <CheckIcon fontSize="small" sx={{ ml: 3 }} />}
					</MenuItem>
				))}
			</Menu>
		</>
	);
}
