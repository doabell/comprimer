fn main() {
    println!("cargo:rerun-if-changed=assets/app.ico");
    println!("cargo:rerun-if-changed=assets/app.manifest");
    winresource::WindowsResource::new()
        .set_icon("assets/app.ico")
        .set_manifest_file("assets/app.manifest")
        .set("ProductName", "Comprimer")
        .set(
            "FileDescription",
            "Comprimer — image compression and conversion",
        )
        .compile()
        .expect("compile Windows icon and application manifest");
}
