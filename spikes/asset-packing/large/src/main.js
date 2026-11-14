// Whole libraries on purpose: namespace objects escape to a global, so the bundler keeps all of each one.
import "bootstrap/dist/css/bootstrap.css";
import "bootstrap-icons/font/bootstrap-icons.css";
import * as bootstrap from "bootstrap";
import * as echarts from "echarts";
import * as THREE from "three";
import React from "react";
import { createRoot } from "react-dom/client";

globalThis.__libs = { bootstrap, echarts, THREE };

const chart = echarts.init(document.getElementById("chart"), null, { renderer: "svg" });
chart.setOption({ xAxis: { data: ["a", "b", "c"] }, yAxis: {}, series: [{ type: "bar", data: [3, 1, 2] }] });
const scene = new THREE.Scene();
scene.add(new THREE.Mesh(new THREE.BoxGeometry(), new THREE.MeshBasicMaterial()));

createRoot(document.getElementById("root")).render(
  React.createElement("p", null, React.createElement("i", { className: "bi bi-check-circle" }), " Large bundle ready"),
);

// Same signal as the React app's SDK: the host measures navigation -> this request.
requestAnimationFrame(() => fetch("/_spar/ready", { method: "POST" }));
