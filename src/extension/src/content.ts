const passwordInputs = document.querySelectorAll<HTMLInputElement>('input[type="password"]');

for (const input of passwordInputs) {
  input.dataset.privoraDetected = "true";
}

