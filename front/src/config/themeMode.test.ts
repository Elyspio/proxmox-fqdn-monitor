import { describe, expect, it } from "vitest";
import { isThemeMode, readStoredMode, resolvePaletteMode } from "./themeMode";

const storageWith = (value: string | null): Pick<Storage, "getItem"> => ({
	getItem: () => value,
});

describe("isThemeMode", () => {
	it("accepts the three known modes", () => {
		expect(isThemeMode("system")).toBe(true);
		expect(isThemeMode("light")).toBe(true);
		expect(isThemeMode("dark")).toBe(true);
	});

	it("rejects anything else", () => {
		expect(isThemeMode("solarized")).toBe(false);
		expect(isThemeMode(null)).toBe(false);
		expect(isThemeMode(undefined)).toBe(false);
	});
});

describe("readStoredMode", () => {
	it("defaults to system when nothing is stored", () => {
		expect(readStoredMode(storageWith(null))).toBe("system");
	});

	it("defaults to system when storage is unavailable", () => {
		expect(readStoredMode(undefined)).toBe("system");
	});

	it("defaults to system when the stored value is corrupt", () => {
		expect(readStoredMode(storageWith("neon"))).toBe("system");
	});

	it("returns a valid stored preference", () => {
		expect(readStoredMode(storageWith("light"))).toBe("light");
	});
});

describe("resolvePaletteMode", () => {
	it("follows the OS when the preference is system", () => {
		expect(resolvePaletteMode("system", true)).toBe("dark");
		expect(resolvePaletteMode("system", false)).toBe("light");
	});

	it("pins the palette when the preference is explicit, ignoring the OS", () => {
		expect(resolvePaletteMode("light", true)).toBe("light");
		expect(resolvePaletteMode("dark", false)).toBe("dark");
	});
});
