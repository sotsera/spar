// typescript-eslint's strict type-checked preset plus React Hooks, as a typical strict React setup.
import tseslint from "typescript-eslint";
import reactHooks from "eslint-plugin-react-hooks";

export default tseslint.config(
  { ignores: ["dist/**"] },
  {
    files: ["src/**/*.{ts,tsx}"],
    extends: [...tseslint.configs.strictTypeChecked],
    plugins: { "react-hooks": reactHooks },
    rules: {
      ...reactHooks.configs.recommended.rules,
      "@typescript-eslint/promise-function-async": "error",
      // SPAR_LINT_THENABLES=1 turns on the option that makes no-floating-promises check thenables such as Call.
      ...(process.env.SPAR_LINT_THENABLES ? { "@typescript-eslint/no-floating-promises": ["error", { checkThenables: true }] } : {}),
    },
    languageOptions: { parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname } },
  },
);
