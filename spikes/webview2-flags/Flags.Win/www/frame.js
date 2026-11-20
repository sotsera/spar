self.sparProbe().then(result => parent.postMessage({ from: 'frame', result }, location.origin));
