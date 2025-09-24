# For x64 linux machine
MRuby::CrossBuild.new("linux-x64") do |conf|
  conf.toolchain :gcc

  conf.gem core: 'mruby-compiler'
  conf.gem core: 'mruby-string-ext'
  # conf.gem core: 'mruby-bin-mrbc'
  conf.gem './mrbgems/mrbcs-compiler'

  conf.disable_presym

  conf.compilers.each do |cc|
    cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM)
    cc.flags << '-fPIC'
  end

  conf.archiver do |archiver|
    archiver.command = cc.command
    archiver.archive_options = '-shared -o %{outfile} %{objs}'
  end

  conf.linker do |linker|
    linker.flags = ['-Wl,--whole-archive']
    linker.libraries = %w(m)
  end

  # file extensions
  conf.exts do |exts|
    exts.library = '.so'
  end
end

MRuby::CrossBuild.new("linux-arm64") do |conf|
  conf.toolchain :gcc

  conf.gem core: 'mruby-compiler'
  conf.gem core: 'mruby-string-ext'
  # conf.gem core: 'mruby-bin-mrbc'  
  conf.gem './mrbgems/mrbcs-compiler'
  
  conf.cc.command = 'aarch64-linux-gnu-gcc'
  conf.linker.command = 'aarch64-linux-gnu-gcc'
  conf.archiver.command = 'aarch64-linux-gnu-ar'

  conf.disable_presym

  conf.compilers.each do |cc|
    cc.defines = %w(MRB_WORD_BOXING MRB_NO_STDIO MRB_NO_PRESYM)
    cc.flags << '-fPIC'
  end

  conf.archiver do |archiver|
    archiver.command = cc.command
    archiver.archive_options = '-shared -o %{outfile} %{objs}'
  end

  conf.linker do |linker|
    linker.flags = ['-Wl,--whole-archive']    
    linker.libraries = %w(m)
  end

  # file extensions
  conf.exts do |exts|
    exts.library = '.so'
  end
end
