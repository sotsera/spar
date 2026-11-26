#!/usr/bin/env bash
source "$EVAL_LIB"
replace Notebook/frontend/src/components/Sidebar.tsx 'import { forwardRef, useMemo } from "react";' 'import { forwardRef, useMemo } from "react";
import logo from "../../assets/logo.svg";'
replace Notebook/frontend/src/components/Sidebar.tsx 'src="https://notebook.example/assets/logo.svg"' 'src={logo}'
